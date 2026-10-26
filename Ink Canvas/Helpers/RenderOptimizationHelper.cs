#pragma warning disable IDE0130 // 命名空间与文件夹结构不匹配（项目约定使用 Ink_Canvas）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
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
