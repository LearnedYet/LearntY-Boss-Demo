using UnityEditor;
using UnityEngine;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 界面绘制使用的精灵，通过生成而非导入获得。
    /// </summary>
    public static class UiSprites
    {
        private const string Folder = "Assets/_Project/Materials/UI";

        /// <summary>
        /// 资源条填充的竖向明暗：顶部细亮边，向底部变暗。
        /// </summary>
        /// <remarks>
        /// 纯色像彩色矩形；有明暗后同一填充像充满的槽。使用白色以接受各条自身着色。
        /// </remarks>
        /// <returns>精灵资产。</returns>
        public static Sprite GaugeShading()
        {
            const int Height = 32;
            string path = Folder + "/GaugeShading.asset";

            Sprite existing = LoadSprite(path);

            if (existing != null)
            {
                return existing;
            }

            var texture = new Texture2D(4, Height, TextureFormat.RGBA32, false)
            {
                name = "GaugeShadingTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            for (int y = 0; y < Height; y++)
            {
                float up = (y + 0.5f) / Height;
                float shade = Mathf.Lerp(0.55f, 1f, up);
                float lit = up > 0.84f && up < 0.94f ? 0.35f : 0f;
                float value = Mathf.Clamp01(shade + lit);

                for (int x = 0; x < 4; x++)
                {
                    texture.SetPixel(x, y, new Color(value, value, value, 1f));
                }
            }

            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, Height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "GaugeShading";

            // 纹理是主资产，精灵存于其中，因此精灵引用的是已保存纹理对象而非仅内存对象。
            AssetAuthoring.EnsureFolderExists(Folder);
            AssetDatabase.CreateAsset(texture, path);
            AssetDatabase.AddObjectToAsset(sprite, texture);
            AssetDatabase.SaveAssets();

            return LoadSprite(path);
        }

        /// <summary>中心向边缘淡至透明的柔白光晕，由使用它的 Image 着色。</summary>
        /// <returns>精灵资产。</returns>
        public static Sprite RadialGlow()
        {
            const int Size = 256;
            string path = Folder + "/RadialGlow.asset";

            Sprite existing = LoadSprite(path);

            if (existing != null)
            {
                return existing;
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "RadialGlowTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x + 0.5f) / Size * 2f - 1f, dy = (y + 0.5f) / Size * 2f - 1f;
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, falloff * falloff * falloff));
                }
            }

            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "RadialGlow";

            AssetAuthoring.EnsureFolderExists(Folder);
            AssetDatabase.CreateAsset(texture, path);
            AssetDatabase.AddObjectToAsset(sprite, texture);
            AssetDatabase.SaveAssets();

            return LoadSprite(path);
        }

        private static Sprite LoadSprite(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite sprite)
                {
                    return sprite;
                }
            }

            return null;
        }
    }
}
