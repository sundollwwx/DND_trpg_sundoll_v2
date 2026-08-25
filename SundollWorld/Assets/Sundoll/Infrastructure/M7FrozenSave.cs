using System;
using System.Collections.Generic;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    [Serializable]
    public sealed class M7FrozenSaveDocument
    {
        public int saveFormatVersion = M7ReleaseContract.SaveFormatVersion;
        public int worldSchemaVersion = M7ReleaseContract.WorldSchemaVersion;
        public string canonicalStateHash;
        public string stateJson;
    }

    public sealed class M7SaveValidationResult
    {
        public bool valid;
        public string canonicalStateHash;
        public string diagnostic;
        public List<string> warnings = new List<string>();
    }

    public static class M7FrozenSave
    {
        public static M7FrozenSaveDocument Freeze(M1WorldState state)
        {
            var validation = M7SaveValidator.Validate(state);
            if (!validation.valid)
            {
                throw new InvalidOperationException(validation.diagnostic);
            }

            return new M7FrozenSaveDocument
            {
                canonicalStateHash = validation.canonicalStateHash,
                stateJson = JsonUtility.ToJson(state, true)
            };
        }

        public static M7SaveValidationResult Validate(M7FrozenSaveDocument document)
        {
            if (document == null || string.IsNullOrWhiteSpace(document.stateJson))
            {
                return new M7SaveValidationResult { valid = false, diagnostic = "Frozen save is empty." };
            }

            if (document.saveFormatVersion != M7ReleaseContract.SaveFormatVersion ||
                document.worldSchemaVersion != M7ReleaseContract.WorldSchemaVersion)
            {
                return new M7SaveValidationResult { valid = false, diagnostic = "Frozen save version is not v1/schema 2." };
            }

            try
            {
                var state = JsonUtility.FromJson<M1WorldState>(document.stateJson);
                var result = M7SaveValidator.Validate(state);
                if (!result.valid)
                {
                    return result;
                }

                if (!string.Equals(result.canonicalStateHash, document.canonicalStateHash, StringComparison.OrdinalIgnoreCase))
                {
                    result.valid = false;
                    result.diagnostic = "Frozen save canonical hash mismatch.";
                }

                return result;
            }
            catch (Exception exception)
            {
                return new M7SaveValidationResult { valid = false, diagnostic = exception.Message };
            }
        }
    }

    public static class M7SaveValidator
    {
        public static M7SaveValidationResult Validate(M1WorldState state)
        {
            if (state == null)
            {
                return new M7SaveValidationResult { valid = false, diagnostic = "World state is null." };
            }

            if (state.schemaVersion != M7ReleaseContract.WorldSchemaVersion)
            {
                return new M7SaveValidationResult
                {
                    valid = false,
                    diagnostic = "World schema is " + state.schemaVersion + "; M7 requires schema " + M7ReleaseContract.WorldSchemaVersion + "."
                };
            }

            if (!M4PieceStateValidator.TryValidate(state, out var pieceDiagnostic))
            {
                return new M7SaveValidationResult { valid = false, diagnostic = pieceDiagnostic };
            }

            if (state.m5Console != null)
            {
                var mapIds = new HashSet<string>(StringComparer.Ordinal);
                if (state.m5Console.maps != null)
                {
                    foreach (var map in state.m5Console.maps)
                    {
                        if (map == null || string.IsNullOrWhiteSpace(map.id) || !mapIds.Add(map.id))
                        {
                            return new M7SaveValidationResult { valid = false, diagnostic = "M5 map IDs must be unique and non-empty." };
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(state.m5Console.activeMapId) && !mapIds.Contains(state.m5Console.activeMapId))
                {
                    return new M7SaveValidationResult { valid = false, diagnostic = "M5 active map is not registered." };
                }
            }

            return new M7SaveValidationResult
            {
                valid = true,
                canonicalStateHash = M2CanonicalStateHasher.Compute(state),
                diagnostic = string.Empty
            };
        }
    }
}
