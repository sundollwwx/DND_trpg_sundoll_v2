using System;
using System.Collections.Generic;
using Sundoll.Application;
using Sundoll.Core;

namespace Sundoll.Presentation
{
    public sealed class StarterContentInstallResult
    {
        private readonly List<string> diagnostics = new List<string>();

        public bool Accepted { get; internal set; } = true;
        public int RegisteredAssets { get; internal set; }
        public int InstalledDefinitions { get; internal set; }
        public int RepairedDefinitions { get; internal set; }
        public int SkippedDefinitions { get; internal set; }
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public bool Changed => RegisteredAssets + InstalledDefinitions + RepairedDefinitions > 0;

        internal void Reject(string diagnostic)
        {
            Accepted = false;
            if (!string.IsNullOrWhiteSpace(diagnostic))
            {
                diagnostics.Add(diagnostic);
            }
        }
    }

    /// <summary>
    /// Imports first-party token bytes through the existing content-addressed
    /// asset catalogue, then creates or repairs definitions exclusively via
    /// the M4 facade and Command Bus. It is safe to run repeatedly.
    /// </summary>
    public static class StarterContentInstaller
    {
        public static StarterContentInstallResult InstallMissing(
            WorkbenchSession session,
            M7StarterContentManifest manifest)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var result = new StarterContentInstallResult();
            foreach (var starterDefinition in manifest.PieceDefinitions)
            {
                try
                {
                    InstallOne(session, starterDefinition, result);
                }
                catch (Exception exception)
                {
                    result.Reject(starterDefinition.DefinitionId + " 安装失败：" + exception.Message);
                }
            }

            return result;
        }

        private static void InstallOne(
            WorkbenchSession session,
            M7StarterPieceDefinition starterDefinition,
            StarterContentInstallResult result)
        {
            var state = session.CommandBus.State;
            var existingDefinition = M4PieceQueries.FindDefinition(state, starterDefinition.DefinitionId);
            var existingBoundAsset = M4PieceQueries.FindAsset(
                state,
                existingDefinition == null ? null : existingDefinition.assetId);
            if (existingDefinition != null && existingBoundAsset != null &&
                session.PieceAssetCatalog.IsAssetAvailable(existingBoundAsset))
            {
                result.SkippedDefinitions++;
                return;
            }

            var pngBytes = M7StarterTokenRenderer.CreatePng(starterDefinition);
            var imported = M4RuntimeImageImporter.Import(
                session.PieceAssetCatalog,
                pngBytes,
                "png",
                "image/png");
            if (!imported.accepted || imported.asset == null)
            {
                throw new InvalidOperationException(imported.diagnostic ?? "内置棋子图片导入失败。");
            }

            var asset = M4PieceQueries.FindAsset(state, imported.asset.id);
            if (asset == null)
            {
                RecordAccepted(session, session.PieceLibrary.RegisterAsset(imported.asset));
                result.RegisteredAssets++;
            }

            if (existingDefinition == null)
            {
                RecordAccepted(session, session.PieceLibrary.CreateDefinition(
                    starterDefinition.DefinitionId,
                    starterDefinition.DisplayName,
                    starterDefinition.Category,
                    starterDefinition.Tags,
                    imported.asset.id));
                result.InstalledDefinitions++;
                return;
            }

            RecordAccepted(session, session.PieceLibrary.UpdateDefinition(
                existingDefinition.id,
                string.IsNullOrWhiteSpace(existingDefinition.displayName)
                    ? starterDefinition.DisplayName
                    : existingDefinition.displayName,
                string.IsNullOrWhiteSpace(existingDefinition.category)
                    ? starterDefinition.Category
                    : existingDefinition.category,
                existingDefinition.tags == null || existingDefinition.tags.Count == 0
                    ? starterDefinition.Tags
                    : existingDefinition.tags,
                imported.asset.id,
                existingDefinition.footprintWidth,
                existingDefinition.footprintHeight));
            result.RepairedDefinitions++;
        }

        private static void RecordAccepted(WorkbenchSession session, M1CommandReceipt receipt)
        {
            if (receipt == null || !receipt.accepted)
            {
                throw new InvalidOperationException(receipt == null ? "命令未返回结果。" : receipt.message);
            }

            session.SaveSession.RecordAccepted(receipt, session.CommandBus.State);
        }
    }
}
