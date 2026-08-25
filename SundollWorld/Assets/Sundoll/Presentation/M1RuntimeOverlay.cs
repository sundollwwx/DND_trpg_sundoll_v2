using System;
using System.IO;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Presentation
{
    public sealed class M1RuntimeOverlay : MonoBehaviour
    {
        private M1CommandBus commandBus;
        private M2SaveSession saveSession;
        private M3RuntimeMapEditor mapEditor;
        private string status = "正在初始化 M1";

        public void Initialize(M1CommandBus commandBus, M2SaveSession saveSession)
        {
            this.commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
            this.saveSession = saveSession ?? throw new ArgumentNullException(nameof(saveSession));
            status = saveSession.LastAction;
            mapEditor = GetComponent<M3RuntimeMapEditor>();
        }

        private void Update()
        {
            if (saveSession != null)
            {
                saveSession.TickAutosave(Time.unscaledDeltaTime);
                saveSession.RefreshSaveStatus();
                status = saveSession.LastAction;
            }
        }

        private void OnDestroy()
        {
            saveSession?.Dispose();
        }

        private void OnGUI()
        {
            if (commandBus == null)
            {
                return;
            }

            var state = commandBus.State;
            GUILayout.BeginArea(new Rect(18, 18, 760, 330), GUI.skin.box);
            GUILayout.Label("SundollWorld · M1 最小纵向闭环", GUI.skin.label);
            GUILayout.Label("纯 C# 状态 → LocalAuthority → Snapshot → View");
            GUILayout.Space(6);
            GUILayout.Label($"Project: {state.project?.displayName} ({state.project?.id})");
            GUILayout.Label($"MapDocument: {state.map?.id} → Published: {state.publishedMap?.id}");
            GUILayout.Label($"Scenario: {state.scenario?.id} → BoardInstance: {state.board?.id}");

            var location = state.pieceInstance?.location;
            var locationText = location == null
                ? "无位置"
                : $"{location.kind} @ ({location.x}, {location.y}) / {location.boardId}";
            GUILayout.Label($"PieceInstance: {state.pieceInstance?.id} → {locationText}");
            GUILayout.Label($"World Revision: {state.revision}");
            GUILayout.Label($"状态：{status}");
            GUILayout.Label($"M2 HEAD：{saveSession.ActiveRevisionId} / 保存状态：{SaveStatusLabel(saveSession.SaveStatus)} / 待自动保存事务：{saveSession.PendingTransactions}");
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("向右移动", GUILayout.Width(110)))
            {
                var nextX = location == null ? 0 : location.x + 1;
                var receipt = commandBus.Execute(new M1MovePieceCommand(
                    Guid.NewGuid().ToString("N"), commandBus.State.revision, nextX, location == null ? 0 : location.y));
                status = receipt.message;
                if (receipt.accepted)
                {
                    saveSession.RecordAccepted(receipt, commandBus.State);
                }
            }

            if (GUILayout.Button("Undo", GUILayout.Width(80)))
            {
                if (commandBus.Undo())
                {
                    saveSession.RecordMutation("undo-" + Guid.NewGuid().ToString("N"), commandBus.LastAction, commandBus.State);
                }
                status = commandBus.LastAction;
            }

            if (GUILayout.Button("Redo", GUILayout.Width(80)))
            {
                if (commandBus.Redo())
                {
                    saveSession.RecordMutation("redo-" + Guid.NewGuid().ToString("N"), commandBus.LastAction, commandBus.State);
                }
                status = commandBus.LastAction;
            }

            if (GUILayout.Button("保存 Snapshot", GUILayout.Width(120)))
            {
                var operation = saveSession.QueueSave("手动保存 Snapshot");
                status = operation.Status == M2SaveStatus.Saving
                    ? "M2 Revision 保存中"
                    : saveSession.LastAction;
            }

            if (GUILayout.Button("重新加载", GUILayout.Width(100)))
            {
                var loadedState = saveSession.Reload().state;
                commandBus = new M1CommandBus(
                    loadedState,
                    new M1LocalAuthority(new AllowAllRulePolicy()));
                if (mapEditor != null)
                {
                    mapEditor.Bind(commandBus, saveSession);
                }

                status = saveSession.LastAction + "，View 使用纯数据重建";
            }

            if (GUILayout.Button("验证存档", GUILayout.Width(90)))
            {
                var validation = saveSession.Validate();
                status = validation.valid
                    ? "存档验证通过：" + validation.saveRevisionId
                    : "存档验证失败：" + validation.diagnostic;
            }

            if (GUILayout.Button("导出 .sundollpkg", GUILayout.Width(120)))
            {
                var parent = Directory.GetParent(saveSession.ProjectRoot).FullName;
                var packagePath = Path.Combine(parent, "SundollWorld-M2.sundollpkg");
                saveSession.ExportPackage(packagePath);
                status = "已导出：" + packagePath;
            }

            GUILayout.EndHorizontal();
            GUILayout.Label($"Project Root: {saveSession.ProjectRoot}");
            GUILayout.Label($"HEAD: {Path.Combine(saveSession.ProjectRoot, "HEAD.json")}");
            GUILayout.EndArea();
        }

        private static string SaveStatusLabel(M2SaveStatus saveStatus)
        {
            switch (saveStatus)
            {
                case M2SaveStatus.Unsaved:
                    return "未落盘";
                case M2SaveStatus.Saving:
                    return "保存中";
                case M2SaveStatus.Safe:
                    return "已安全保存";
                case M2SaveStatus.Failed:
                    return "保存失败";
                default:
                    return saveStatus.ToString();
            }
        }
    }
}
