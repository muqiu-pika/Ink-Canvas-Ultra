using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 通过 ROT（Running Object Table）枚举正在运行的 PowerPoint / WPS 实例，
    /// 按"放映优先级"挑选最佳连接目标：1 = 有 ActivePresentation，
    /// 2 = 有 SlideShowWindow，3 = 放映窗口处于前台。多开演示文稿时优先连到正在放映的那个。
    ///
    /// 算法移植自社区版 Helpers\PPTROTConnectionHelper.cs，针对 net472 改写：
    /// 1. 以 System.Runtime.InteropServices.ComTypes（BCL 自带）替代 CsWin32 的
    ///    Windows.Win32 接口（IRunningObjectTable/IBindCtx/IMoniker/IEnumMoniker 同名）。
    /// 2. PInvoke.* 改为 DllImport（ole32/user32）；CLSIDFromProgID 改用
    ///    Type.GetTypeFromProgID(progId).GUID（等价且无需额外 P/Invoke）。
    /// 3. moniker[0].GetDisplayName(ctx, null, (PWSTR*)&ptr) 改为 BCL 的
    ///    out string 重载，去掉 unsafe。互操作约定：COM 方法按 CoTaskMemAlloc
    ///    分配出参字符串，BCL 互操作 marshaller 在复制成托管 string 后会自动
    ///    CoTaskMemFree 释放原生缓冲，因此这里无需（也不应）再手动释放，
    ///    源文件中的 FreeCoTaskMem 在本端口中不存在泄漏路径。
    /// 4. IEnumMoniker.Next 的 pceltFetched 以 IntPtr 接收（AllocCoTaskMem(4) +
    ///    Marshal.ReadInt32），finally 中释放。
    ///
    /// 本类只负责"连接"，不含 WPS 进程查杀、窗口枚举、System.Timers 逻辑；
    /// 全程吞异常降级：任何异常写日志后返回 null，交由上层 COM 轮询兜底。
    /// 每个 COM 引用（GetObject/EnumRunning/ActivePresentation/SlideShowWindow 等）
    /// 均在 finally 中经 SafeReleaseComObject 配平释放，避免 PPT/WPS 进程残留。
    /// </summary>
    public static class PPTROTConnectionHelper
    {
        #region Win32 API 声明（net472）

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        #endregion

        #region 常量

        /// <summary>演示文稿类文件名扩展名（moniker 以文件路径呈现时用于识别）。</summary>
        private static readonly string[] PPTLikeExtensions = new[]
        {
            ".pptx", ".pptm", ".ppt",
            ".ppsx", ".ppsm", ".pps",
            ".potx", ".potm", ".pot",
            ".dps", ".dpt"
        };

        /// <summary>硬编码 fallback 应用 moniker（Office PowerPoint 与 WPS 演示）。</summary>
        private static readonly string OfficeFallbackApplicationMoniker = "!{91493441-5A91-11CF-8700-00AA0060263B}";
        private static readonly string WpsFallbackApplicationMoniker = "!{44720441-94BF-4940-926D-4F38FECF2A48}";

        #endregion

        #region 公开入口

        /// <summary>
        /// 在 ROT 中查找"最佳"的 PowerPoint/WPS 实例并返回。
        /// </summary>
        /// <param name="isSupportWPS">false 时不解析 WPS 的 ProgID / fallback moniker / 双进程特判。</param>
        /// <param name="priority">命中实例的优先级（1=有活动演示，2=在放映，3=放映窗口前台）；0 表示未命中。</param>
        /// <returns>Interop Application 实例；未命中、类型不匹配或发生异常时返回 null（priority = 0）。</returns>
        public static Microsoft.Office.Interop.PowerPoint.Application TryGetBestSlideShowApplication(bool isSupportWPS, out int priority)
        {
            object bestApp = GetAnyActivePowerPoint(isSupportWPS, out priority);

            if (bestApp == null || priority <= 0)
            {
                return null;
            }

            // ROT 里拿到的可能是 Office，也可能是 WPS（二进制兼容接口），
            // 统一按 Interop 接口做 QueryInterface 转换；转换失败则释放并视为未命中。
            if (bestApp is Microsoft.Office.Interop.PowerPoint.Application pptApp)
            {
                return pptApp;
            }

            SafeReleaseComObject(bestApp);
            priority = 0;
            return null;
        }

        #endregion

        /// <summary>
        /// 枚举 ROT，按优先级评分挑出最佳实例。算法与源文件 GetAnyActivePowerPoint 一致
        /// （去除 targetApp/targetPriority 形参与 unsafe）。
        /// </summary>
        private static object GetAnyActivePowerPoint(bool isSupportWPS, out int bestPriority)
        {
            IRunningObjectTable rot = null;
            IEnumMoniker enumMoniker = null;
            IntPtr pceltFetched = IntPtr.Zero;

            object bestApp = null;
            bestPriority = 0;
            int highestPriority = 0;

            List<object> foundAppObjects = new List<object>();

            try
            {
                int hr = GetRunningObjectTable(0, out rot);
                if (hr != 0 || rot == null)
                {
                    LogHelper.WriteLogToFile("PPT ROT：无法获取 Running Object Table", LogHelper.LogType.Warning);
                    return null;
                }

                rot.EnumRunning(out enumMoniker);
                if (enumMoniker == null)
                {
                    LogHelper.WriteLogToFile("PPT ROT：无法枚举 ROT 中的对象", LogHelper.LogType.Warning);
                    return null;
                }

                pceltFetched = Marshal.AllocCoTaskMem(sizeof(int));
                IMoniker[] moniker = new IMoniker[1];
                string[] applicationMonikersFromProgIds = GetApplicationMonikersFromProgIds(isSupportWPS);
                List<string> fallbackMonikers = new List<string> { OfficeFallbackApplicationMoniker };
                if (isSupportWPS)
                {
                    fallbackMonikers.Add(WpsFallbackApplicationMoniker);
                }

                while (enumMoniker.Next(1, moniker, pceltFetched) == 0 && Marshal.ReadInt32(pceltFetched) == 1)
                {
                    IBindCtx bindCtx = null;
                    object comObject = null;
                    dynamic candidateApp = null;
                    string displayName = "Unknown";
                    dynamic activePres = null;
                    dynamic ssWindow = null;
                    bool keepAlive = false;

                    try
                    {
                        int bindHr = CreateBindCtx(0, out bindCtx);
                        if (bindHr != 0 || bindCtx == null || moniker[0] == null) continue;

                        try
                        {
                            // BCL 签名：void GetDisplayName(IBindCtx, IMoniker, out string)。
                            // 由 marshaller 按 CoTaskMem 约定分配并释放原生串，见类注释第 3 条。
                            moniker[0].GetDisplayName(bindCtx, null, out displayName);
                        }
                        catch
                        {
                            displayName = "Unknown";
                        }

                        bool looksLikePresentationFile = LooksLikePresentationFile(displayName);
                        bool isApplicationMoniker = ContainsMoniker(applicationMonikersFromProgIds, displayName);
                        if (!isApplicationMoniker)
                        {
                            isApplicationMoniker = ContainsMoniker(fallbackMonikers, displayName);
                        }

                        if (looksLikePresentationFile || isApplicationMoniker)
                        {
                            try
                            {
                                rot.GetObject(moniker[0], out comObject);
                            }
                            catch
                            {
                                comObject = null;
                            }

                            if (comObject != null)
                            {
                                if (isApplicationMoniker)
                                {
                                    candidateApp = comObject;
                                    comObject = null;
                                }
                                else
                                {
                                    try
                                    {
                                        object appObj = comObject.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, comObject, null);
                                        candidateApp = appObj;
                                    }
                                    catch
                                    {
                                        // 文件 moniker 对应的对象没有 Application 属性：忽略
                                    }
                                }
                            }
                        }

                        bool isDuplicate = false;
                        if (candidateApp != null)
                        {
                            foreach (var processedApp in foundAppObjects)
                            {
                                if (AreComObjectsEqual((object)candidateApp, processedApp))
                                {
                                    isDuplicate = true;
                                    break;
                                }
                            }

                            if (!isDuplicate)
                            {
                                foundAppObjects.Add(candidateApp);
                                keepAlive = true;
                            }
                        }

                        if (candidateApp != null && !isDuplicate)
                        {
                            int currentPriority = 0;

                            try
                            {
                                try
                                {
                                    activePres = candidateApp.ActivePresentation;
                                }
                                catch
                                {
                                    activePres = null;
                                }

                                if (activePres != null)
                                {
                                    currentPriority = 1;

                                    try
                                    {
                                        ssWindow = activePres.SlideShowWindow;
                                    }
                                    catch
                                    {
                                        ssWindow = null;
                                    }

                                    if (ssWindow != null)
                                    {
                                        currentPriority = 2;

                                        try
                                        {
                                            bool isActive = false;
                                            try
                                            {
                                                object val = ssWindow.Active;
                                                if (val is int && (int)val == -1) isActive = true;
                                                else if (val is bool && (bool)val == true) isActive = true;
                                            }
                                            catch
                                            {
                                                isActive = false;
                                            }

                                            if (isActive)
                                            {
                                                currentPriority = 3;
                                            }
                                            else
                                            {
                                                if (IsSlideShowWindowActive(ssWindow))
                                                {
                                                    currentPriority = 3;
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            // 读取放映窗口状态失败：维持当前优先级
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogHelper.WriteLogToFile($"PPT ROT：计算优先级时出错 {ex.Message}", LogHelper.LogType.Warning);
                            }

                            if (currentPriority > 0 && currentPriority > highestPriority)
                            {
                                highestPriority = currentPriority;
                                SafeReleaseComObject(bestApp);
                                bestApp = candidateApp;
                                candidateApp = null;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"PPT ROT：枚举循环中出错 {ex.Message}", LogHelper.LogType.Warning);
                    }
                    finally
                    {
                        SafeReleaseComObject(ssWindow);
                        SafeReleaseComObject(activePres);

                        if (!keepAlive)
                        {
                            SafeReleaseComObject(candidateApp);
                        }

                        CleanUpLoopObjects(bindCtx, moniker[0], comObject);
                    }
                }

                bestPriority = highestPriority;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PPT ROT：扫描关键错误 {ex}", LogHelper.LogType.Error);
            }
            finally
            {
                if (foundAppObjects != null)
                {
                    foreach (var cachedApp in foundAppObjects)
                    {
                        if (bestApp != null && ReferenceEquals(cachedApp, bestApp))
                            continue;

                        SafeReleaseComObject(cachedApp);
                    }
                    foundAppObjects.Clear();
                }

                if (enumMoniker != null) Marshal.ReleaseComObject(enumMoniker);
                if (rot != null) Marshal.ReleaseComObject(rot);

                if (pceltFetched != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pceltFetched);
                    pceltFetched = IntPtr.Zero;
                }
            }

            return bestApp;
        }

        /// <summary>用 IUnknown 指针比较两个 COM 对象身份，避免同一实例被重复计入。</summary>
        private static bool AreComObjectsEqual(object o1, object o2)
        {
            if (o1 == null || o2 == null) return false;
            if (ReferenceEquals(o1, o2)) return true;

            IntPtr pUnk1 = IntPtr.Zero;
            IntPtr pUnk2 = IntPtr.Zero;
            try
            {
                pUnk1 = Marshal.GetIUnknownForObject(o1);
                pUnk2 = Marshal.GetIUnknownForObject(o2);
                return pUnk1 == pUnk2;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pUnk1 != IntPtr.Zero) Marshal.Release(pUnk1);
                if (pUnk2 != IntPtr.Zero) Marshal.Release(pUnk2);
            }
        }

        private static bool LooksLikePresentationFile(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return false;

            string lower = displayName.ToLowerInvariant();
            foreach (var ext in PPTLikeExtensions)
            {
                if (lower.Contains(ext))
                    return true;
            }

            return false;
        }

        /// <summary>解析 ProgID 对应 CLSID 生成 "!{GUID}" moniker 列表；未注册的 ProgID 直接跳过。</summary>
        private static string[] GetApplicationMonikersFromProgIds(bool isSupportWPS)
        {
            List<string> progIds = new List<string> { "PowerPoint.Application" };
            if (isSupportWPS)
            {
                progIds.Add("KWPP.Application");
                progIds.Add("Wpp.Application");
                progIds.Add("WPP.Application");
            }

            List<string> monikers = new List<string>();

            foreach (string progId in progIds)
            {
                Guid clsid = GetClsidFromProgId(progId);
                if (clsid != Guid.Empty)
                {
                    string moniker = "!" + clsid.ToString("B").ToUpperInvariant();
                    if (!ContainsMoniker(monikers, moniker))
                    {
                        monikers.Add(moniker);
                    }
                }
            }

            return monikers.ToArray();
        }

        /// <summary>CLSID 解析：net472 下用 Type.GetTypeFromProgID（未注册返回 null，不抛异常），等价于 CLSIDFromProgID。</summary>
        private static Guid GetClsidFromProgId(string progId)
        {
            try
            {
                Type type = Type.GetTypeFromProgID(progId, false);
                if (type != null) return type.GUID;
            }
            catch
            {
                // ProgID 未注册/解析失败：视为无此候选
            }

            return Guid.Empty;
        }

        private static bool ContainsMoniker(IEnumerable<string> monikers, string displayName)
        {
            if (monikers == null || string.IsNullOrEmpty(displayName))
                return false;

            foreach (string moniker in monikers)
            {
                if (string.Equals(displayName, moniker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 放映窗口是否处于前台：前台窗口 PID == 放映窗口 PID，
        /// 或 WPS 双进程特判（前台为 wps 进程、放映窗口为 wpp 进程）。
        /// </summary>
        private static bool IsSlideShowWindowActive(object sswObj)
        {
            try
            {
                IntPtr foregroundHwnd = GetForegroundWindow();
                if (foregroundHwnd == IntPtr.Zero) return false;

                uint fgPid;
                GetWindowThreadProcessId(foregroundHwnd, out fgPid);

                IntPtr sswHwnd = IntPtr.Zero;
                try
                {
                    sswHwnd = GetPPTHwndFromSlideShowWindow(sswObj);
                }
                catch
                {
                    return false;
                }
                if (sswHwnd == IntPtr.Zero) return false;

                uint sswPid;
                GetWindowThreadProcessId(sswHwnd, out sswPid);

                if (fgPid == sswPid) return true;

                try
                {
                    using (Process fgProc = Process.GetProcessById((int)fgPid))
                    using (Process appProc = Process.GetProcessById((int)sswPid))
                    {
                        string fgName = fgProc.ProcessName.ToLower();
                        string appName = appProc.ProcessName.ToLower();

                        if (fgName.StartsWith("wps") && appName.StartsWith("wpp"))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // 进程已退出等：视为不活跃
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr GetPPTHwndFromSlideShowWindow(object pptSlideShowWindowObj)
        {
            IntPtr hwnd = IntPtr.Zero;
            if (pptSlideShowWindowObj == null) return IntPtr.Zero;

            try
            {
                Microsoft.Office.Interop.PowerPoint.SlideShowWindow slideWindow =
                    (Microsoft.Office.Interop.PowerPoint.SlideShowWindow)pptSlideShowWindowObj;

                int hwndVal = slideWindow.HWND;

                hwnd = new IntPtr(hwndVal);
            }
            catch
            {
                hwnd = IntPtr.Zero;
            }

            return hwnd;
        }

        private static void SafeReleaseComObject(object comObj)
        {
            if (comObj == null) return;

            if (Marshal.IsComObject(comObj))
            {
                try
                {
                    Marshal.ReleaseComObject(comObj);
                }
                catch
                {
                    // 引用计数已归零等：忽略
                }
            }
        }

        private static void CleanUpLoopObjects(IBindCtx bindCtx, IMoniker moniker, object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
            if (moniker != null)
                Marshal.ReleaseComObject(moniker);
            if (bindCtx != null)
                Marshal.ReleaseComObject(bindCtx);
        }
    }
}
