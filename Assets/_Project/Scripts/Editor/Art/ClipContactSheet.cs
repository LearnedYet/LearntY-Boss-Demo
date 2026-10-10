using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 把每个许可动画片段渲染为一条姿势图，便于通过观察选择片段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 资源包的文件名几乎说明不了什么： <c>slash</c>, <c>slash (2)</c> 和 <c>slash (5)</c> 分别是快
    /// 斩、三秒半的连击和宽幅横扫；这种差异决定轻攻击是否看起来够轻。每个片段在所属角色上取五个姿势，
    /// 从左前方渲染，同时展示前摇和伸展范围。
    /// </para>
    /// <para>
    /// 需要 GPU，因此从未使用以下参数启动的批处理编辑器运行： <c>-nographics</c>。帧以原始 RGBA 写出：
    /// 先是八字节的宽高头部，再是像素行。项目未包含图像编码所需的引擎模块，
    /// 故由 Unity 外部脚本拼接成图表。
    /// </para>
    /// </remarks>
    public static class ClipContactSheet
    {
        private const int Size = 256;
        private static readonly float[] Samples = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        /// <summary>渲染每个片段。输出文件夹由以下参数之后的参数指定： <c>-contactSheetOut</c>.</summary>
        public static void Render()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogError("[ART] The contact sheet needs a GPU. Run the editor without -nographics.");
                EditorApplication.Exit(2);
                return;
            }

            string[] args = System.Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-contactSheetOut");
            string output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.GetTempPath();
            Directory.CreateDirectory(output);

            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);

            var cameraObject = new GameObject("ContactSheetCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.18f, 0.2f);
            camera.fieldOfView = 30f;

            var lightObject = new GameObject("ContactSheetLight");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            lightObject.transform.rotation = Quaternion.Euler(40f, 150f, 0f);

            int rendered = 0;

            try
            {
                foreach (string file in Directory.GetFiles(LicensedArtPostprocessor.AnimationFolder, "*.fbx", SearchOption.AllDirectories).OrderBy(f => f))
                {
                    string path = file.Replace('\\', '/');
                    AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                        .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

                    if (clip == null)
                    {
                        continue;
                    }

                    string set = Path.GetFileName(Path.GetDirectoryName(path));
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(LicensedArtPostprocessor.CharacterFor(path));
                    GameObject body = UnityEngine.Object.Instantiate(model);

                    try
                    {
                        float height = set == "Boss" ? 2.45f : 1.82f;
                        Transform hips = body.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Hips);

                        for (int i = 0; i < Samples.Length; i++)
                        {
                            clip.SampleAnimation(body, Samples[i] * clip.length);

                            // 直接采样片段会播放游戏中已去除的根运动，使突进角色走出画面。
                            // 摄像机因此跟随髋部：这里观察姿势而非位移。
                            // 采用足够近的左前方四分之三视角，便于辨认动作。
                            var focus = new Vector3(hips.position.x, hips.position.y * 0.9f, hips.position.z);
                            cameraObject.transform.position = focus + new Vector3(-height * 1.1f, height * 0.3f, height * 1.6f);
                            cameraObject.transform.LookAt(focus);

                            camera.Render();

                            RenderTexture.active = target;
                            readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                            readback.Apply();
                            RenderTexture.active = null;

                            WriteRaw(Path.Combine(output, $"{set}__{clip.name}__{i}.raw"), readback);
                        }

                        rendered++;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(body);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(lightObject);
                UnityEngine.Object.DestroyImmediate(readback);
                target.Release();
            }

            Debug.Log($"[ART] Contact sheet: {rendered} clips written to {output}");
        }

        private static void WriteRaw(string path, Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            var bytes = new byte[8 + pixels.Length * 4];

            BitConverter.GetBytes(texture.width).CopyTo(bytes, 0);
            BitConverter.GetBytes(texture.height).CopyTo(bytes, 4);

            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[8 + i * 4] = pixels[i].r;
                bytes[9 + i * 4] = pixels[i].g;
                bytes[10 + i * 4] = pixels[i].b;
                bytes[11 + i * 4] = pixels[i].a;
            }

            File.WriteAllBytes(path, bytes);
        }
    }
}
