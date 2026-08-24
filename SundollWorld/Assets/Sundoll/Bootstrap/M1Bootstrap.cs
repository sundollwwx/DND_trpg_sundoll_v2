using System.IO;
using Sundoll.Application;
using Sundoll.Infrastructure;
using Sundoll.Presentation;
using UnityEngine;

namespace Sundoll.Bootstrap
{
    public sealed class M1Bootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (FindFirstObjectByType<M1Bootstrap>() != null)
            {
                return;
            }

            var bootstrapObject = new GameObject("M1Bootstrap");
            DontDestroyOnLoad(bootstrapObject);
            bootstrapObject.AddComponent<M1Bootstrap>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            var commandBus = M1VerticalSlice.CreateDemoBus();
            var projectRoot = Path.Combine(UnityEngine.Application.persistentDataPath, "SundollWorld", "SundollWorld");
            var saveSession = M2SaveSession.Open(projectRoot, commandBus.State);
            var loadedState = saveSession.State;
            var loadedBus = new M1CommandBus(
                loadedState,
                new M1LocalAuthority(new AllowAllRulePolicy()));
            var mapEditor = gameObject.AddComponent<M3RuntimeMapEditor>();
            mapEditor.Bind(loadedBus, saveSession);
            var overlay = gameObject.AddComponent<M1RuntimeOverlay>();
            overlay.Initialize(loadedBus, saveSession);
        }
    }
}
