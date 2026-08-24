#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sundoll.M0Spike
{
    /// <summary>
    /// Disposable, editor-only surface for the M0 manual UI checks.
    /// It deliberately owns no project or runtime state.
    /// </summary>
    public sealed class M0WorkbenchProbeWindow : EditorWindow
    {
        private const string DropProbePath = "Assets/M0Spike/Editor/M0DropProbe.png";

        private M0ProbeAsset probeAsset;
        private SerializedObject serializedProbe;
        private ObjectField textureField;
        private Label dropStatus;
        private VisualElement dropZone;

        [MenuItem("M0 Spike/Open Workbench Probe")]
        private static void Open()
        {
            var window = GetWindow<M0WorkbenchProbeWindow>();
            window.titleContent = new GUIContent("M0 工作台探针");
            window.minSize = new Vector2(420f, 520f);
            window.Show();
        }

        [MenuItem("M0 Spike/Create Drop Probe PNG")]
        private static void CreateDropProbePng()
        {
            var fullPath = Path.GetFullPath(DropProbePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[32 * 32];
                for (var y = 0; y < 32; y++)
                {
                    for (var x = 0; x < 32; x++)
                    {
                        var checker = ((x / 8) + (y / 8)) % 2 == 0;
                        pixels[y * 32 + x] = checker
                            ? new Color32(54, 190, 176, 255)
                            : new Color32(25, 57, 77, 255);
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            }
            finally
            {
                DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(DropProbePath, ImportAssetOptions.ForceUpdate);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(DropProbePath);
            Debug.Log($"M0_WORKBENCH_DROP_PROBE={DropProbePath}");
        }

        [MenuItem("M0 Spike/Delete Drop Probe PNG")]
        private static void DeleteDropProbePng()
        {
            if (AssetDatabase.DeleteAsset(DropProbePath))
            {
                Debug.Log($"M0_WORKBENCH_DROP_PROBE_DELETED={DropProbePath}");
            }
        }

        private void CreateGUI()
        {
            probeAsset = CreateInstance<M0ProbeAsset>();
            probeAsset.title = "中文工作台";
            probeAsset.revision = 3;
            probeAsset.hideFlags = HideFlags.HideAndDontSave;
            serializedProbe = new SerializedObject(probeAsset);
            serializedProbe.Update();

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.paddingLeft = 14;
            scroll.style.paddingRight = 14;
            scroll.style.paddingTop = 12;
            scroll.style.paddingBottom = 14;
            rootVisualElement.Add(scroll);

            var header = new Label("M0 可见工作台测试面板");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 18;
            scroll.Add(header);

            var description = new Label("临时 EditorWindow：验证 UI Toolkit、中文文本、Inspector 绑定和真实拖放。\n该面板不写入正式产品数据。");
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginTop = 6;
            description.style.marginBottom = 12;
            scroll.Add(description);

            var inspectorGroup = new Foldout { text = "Inspector 绑定", value = true };
            var titleProperty = new PropertyField(serializedProbe.FindProperty(nameof(M0ProbeAsset.title)), "名称");
            var revisionProperty = new PropertyField(serializedProbe.FindProperty(nameof(M0ProbeAsset.revision)), "Revision");
            inspectorGroup.Add(titleProperty);
            inspectorGroup.Add(revisionProperty);
            var manualTitle = new TextField("手动名称") { value = probeAsset.title };
            manualTitle.RegisterValueChangedCallback(change =>
            {
                serializedProbe.Update();
                serializedProbe.FindProperty(nameof(M0ProbeAsset.title)).stringValue = change.newValue;
                serializedProbe.ApplyModifiedProperties();
            });
            inspectorGroup.Add(manualTitle);
            inspectorGroup.Add(new Button(ReadBoundValues) { text = "读取绑定值" });
            titleProperty.Bind(serializedProbe);
            revisionProperty.Bind(serializedProbe);
            scroll.Add(inspectorGroup);

            textureField = new ObjectField("图片对象")
            {
                objectType = typeof(Texture2D),
                allowSceneObjects = false
            };
            textureField.RegisterValueChangedCallback(change =>
            {
                dropStatus.text = change.newValue == null
                    ? "图片对象已清空"
                    : $"图片对象已绑定：{change.newValue.name}";
            });
            scroll.Add(textureField);

            dropZone = new VisualElement();
            dropZone.style.minHeight = 120;
            dropZone.style.marginTop = 8;
            dropZone.style.marginBottom = 8;
            dropZone.style.paddingTop = 22;
            dropZone.style.paddingBottom = 22;
            dropZone.style.paddingLeft = 12;
            dropZone.style.paddingRight = 12;
            dropZone.style.alignItems = Align.Center;
            dropZone.style.justifyContent = Justify.Center;
            dropZone.style.borderTopWidth = 2;
            dropZone.style.borderBottomWidth = 2;
            dropZone.style.borderLeftWidth = 2;
            dropZone.style.borderRightWidth = 2;
            dropZone.style.borderTopColor = new Color(0.21f, 0.74f, 0.69f);
            dropZone.style.borderBottomColor = new Color(0.21f, 0.74f, 0.69f);
            dropZone.style.borderLeftColor = new Color(0.21f, 0.74f, 0.69f);
            dropZone.style.borderRightColor = new Color(0.21f, 0.74f, 0.69f);
            dropZone.style.backgroundColor = new Color(0.08f, 0.14f, 0.18f);
            dropZone.Add(new Label("把 PNG/JPG 从 Finder 或 Project 拖到这里"));
            dropZone.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            dropZone.RegisterCallback<DragPerformEvent>(OnDragPerform);
            dropZone.RegisterCallback<DragLeaveEvent>(_ => SetDropVisual(false));
            scroll.Add(dropZone);

            dropStatus = new Label("尚未接收图片");
            dropStatus.style.whiteSpace = WhiteSpace.Normal;
            dropStatus.style.marginBottom = 8;
            scroll.Add(dropStatus);

            scroll.Add(new Button(CreateDropProbePng) { text = "生成 32×32 测试 PNG" });
            scroll.Add(new Button(() => textureField.value = null) { text = "清空图片绑定" });

            var footer = new Label("验收：修改名称/Revision 后点“读取绑定值”；再拖放图片，确认状态文本和图片对象发生变化。");
            footer.style.whiteSpace = WhiteSpace.Normal;
            footer.style.marginTop = 12;
            scroll.Add(footer);

        }

        private void OnDisable()
        {
            if (probeAsset != null)
            {
                DestroyImmediate(probeAsset);
                probeAsset = null;
            }
        }

        private void ReadBoundValues()
        {
            serializedProbe.Update();
            dropStatus.text = $"绑定值：名称 = {probeAsset.title}；Revision = {probeAsset.revision}";
        }

        private void OnDragUpdated(DragUpdatedEvent _)
        {
            if (HasSupportedPayload())
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                SetDropVisual(true);
            }
            else
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            }
        }

        private void OnDragPerform(DragPerformEvent eventArgs)
        {
            if (!HasSupportedPayload())
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            SetDropVisual(false);

            var path = FirstImagePath();
            if (!string.IsNullOrEmpty(path))
            {
                dropStatus.text = $"已接收图片路径：{path}";
            }

            foreach (var reference in DragAndDrop.objectReferences)
            {
                if (reference is Texture2D texture)
                {
                    textureField.value = texture;
                    dropStatus.text = $"已绑定图片：{AssetDatabase.GetAssetPath(texture)}";
                    break;
                }
            }

            eventArgs.StopPropagation();
        }

        private bool HasSupportedPayload()
        {
            return !string.IsNullOrEmpty(FirstImagePath()) || HasTextureReference();
        }

        private string FirstImagePath()
        {
            foreach (var path in DragAndDrop.paths)
            {
                if (IsImagePath(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private bool HasTextureReference()
        {
            foreach (var reference in DragAndDrop.objectReferences)
            {
                if (reference is Texture2D)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsImagePath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        private void SetDropVisual(bool active)
        {
            if (dropZone == null)
            {
                return;
            }

            dropZone.style.backgroundColor = active
                ? new Color(0.12f, 0.32f, 0.28f)
                : new Color(0.08f, 0.14f, 0.18f);
        }
    }
}
#endif
