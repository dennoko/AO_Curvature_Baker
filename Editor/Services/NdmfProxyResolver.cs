using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Resolves NDMF preview proxy renderers via reflection.
    /// Falls back gracefully when NDMF is not installed or no preview session is active.
    /// Internal API — may break on NDMF version upgrades.
    /// </summary>
    internal static class NdmfProxyResolver
    {
        private static bool _initialized;
        private static PropertyInfo _currentProp;
        private static PropertyInfo _mapProp;

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                var asm = Assembly.Load("nadena.dev.ndmf");
                var sessionType = asm?.GetType("nadena.dev.ndmf.preview.PreviewSession");
                if (sessionType == null) return;
                _currentProp = sessionType.GetProperty(
                    "Current", BindingFlags.Public | BindingFlags.Static);
                _mapProp = sessionType.GetProperty(
                    "OriginalToProxyRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch { }
        }

        /// <summary>
        /// Returns the NDMF preview proxy renderer for the given original renderer,
        /// or null if NDMF is not installed, no preview session is active, or no proxy exists.
        /// </summary>
        public static Renderer GetProxyRenderer(Renderer original)
        {
            if (original == null) return null;
            EnsureInitialized();
            if (_currentProp == null || _mapProp == null) return null;

            try
            {
                var session = _currentProp.GetValue(null);
                if (session == null) return null;

                var map = _mapProp.GetValue(session) as IReadOnlyDictionary<Renderer, Renderer>;
                if (map != null && map.TryGetValue(original, out var proxy))
                    return proxy;
            }
            catch { }

            return null;
        }
    }
}
