#pragma warning disable IDE0130 // 命名空间与文件夹结构不匹配（项目约定使用 Ink_Canvas）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 为「插入到画布上的静态内容图片」统一设置高质量缩放 + 渲染缓存。
    ///
    /// 背景：文档照片、截图、粘贴图等内容图片默认使用 HighQuality 缩放以保证清晰度（教学批注场景
    /// 对清晰度敏感，不可直接降级）。但 HighQuality 重采样在每次墨迹重绘（笔迹叠加在图片上方）时都会被
    /// 重新执行，是 GPU/CPU 的无谓开销——书写时图片本身并未改变，重算纯属浪费。
    ///
    /// 做法：配合 CachingHint.Cache 把重采样结果缓存起来。清晰度与原来完全一致，但仅在图片自身发生
    /// 变换（移动/缩放/旋转）或 Source 变更时才重算；纯书写场景下直接复用缓存，显著降低每帧重绘成本。
    ///
    /// 重要：每帧刷新的动态预览图（摄像头画面 currentCameraImage、照片帧 currentPhotoImage）**不可**使用本方法，
    /// 否则缓存会使其画面冻结。本方法只用于一次性插入、之后基本静止的内容图片。
    /// </summary>
    public static class RenderOptimizationHelper
    {
        /// <summary>
        /// 对静态内容图片启用「HighQuality 缩放 + 渲染缓存」。
        /// 缓存失效阈值设为 0.5~2.0：在 0.5x~2.0x 的轻微缩放范围内复用缓存，避免手指缩放时频繁重算。
        /// </summary>
        public static void EnableHighQualityCaching(Image image)
        {
            if (image == null) return;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            RenderOptions.SetCachingHint(image, CachingHint.Cache);
            RenderOptions.SetCacheInvalidationThresholdMinimum(image, 0.5);
            RenderOptions.SetCacheInvalidationThresholdMaximum(image, 2.0);
        }
    }
}
