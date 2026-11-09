using System;
using UnityEngine;

namespace QLNPackBootstrap
{
    public sealed class PackBootstrapMod : IModApi
    {
        public static PackBootstrapBehaviour Behaviour;

        public void InitMod(Mod _modInstance)
        {
            try
            {
                if (GameManager.IsDedicatedServer)
                {
                    return;
                }

                PackLogger.Info("InitMod from " + _modInstance.Path);

                GameObject gameObject = new GameObject("QLNPackBootstrap");
                UnityEngine.Object.DontDestroyOnLoad(gameObject);

                Behaviour = gameObject.AddComponent<PackBootstrapBehaviour>();

                PackConfig config = PackConfig.Load(PackPaths.ConfigFile);
                PackPaths.ModsRootOverride = config.ModsRoot;
                Behaviour.Initialize(config);

                ModEvents.MainMenuOpened.RegisterHandler(OnMainMenuOpened);
                PackLogger.Info("Bootstrap initialized.");
            }
            catch (Exception exception)
            {
                PackLogger.Exception("InitMod failed", exception);
            }
        }

        private static void OnMainMenuOpened(ref ModEvents.SMainMenuOpenedData data)
        {
            try
            {
                if (Behaviour != null)
                {
                    Behaviour.OnMainMenuOpened();
                }
            }
            catch (Exception exception)
            {
                PackLogger.Exception("OnMainMenuOpened failed", exception);
            }
        }
    }
}
