// Copyright (c) 2026 Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnaPartidaMas.Valkarn.Editor;

namespace UnaPartidaMas.Valkarn.Tasks.Editor
{
    /// <summary>
    /// Valkarn Tasks panel for the Valkarn Hub.
    /// Shows pool state and runtime configuration.
    /// </summary>
    public sealed class TasksTrackerPanel : IValkarnEditorPanel
    {
        public string Title     => "Tasks";
        public string PackageId => "com.unapartidamas.valkarn.tasks";
        public int    Order     => 0;

        float refreshInterval = 0.5f;
        double lastRefreshTime;
        bool showPools  = true;
        bool showConfig = false;
        List<(Type type, int size, int maxSize)> cachedPoolInfo = new();

        public void OnEnable()
        {
            EditorApplication.update += Tick;
        }

        public void OnDisable()
        {
            EditorApplication.update -= Tick;
        }

        void Tick()
        {
            if (EditorApplication.timeSinceStartup - lastRefreshTime >= refreshInterval)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                Refresh();
            }
        }

        void Refresh()
        {
            cachedPoolInfo.Clear();
            try
            {
                foreach (var info in ValkarnTask.GetPoolInfo())
                    cachedPoolInfo.Add(info);
            }
            catch { }
        }

        public void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.Space(4);

            if (showPools)
                DrawPools();

            if (showConfig)
                DrawConfig();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            showPools  = GUILayout.Toggle(showPools,  "Pools",  EditorStyles.toolbarButton);
            showConfig = GUILayout.Toggle(showConfig, "Config", EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Refresh: {refreshInterval:F1}s", GUILayout.Width(90));
            refreshInterval = EditorGUILayout.Slider(refreshInterval, 0.1f, 5f, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
        }

        void DrawPools()
        {
            EditorGUILayout.LabelField("Pool Status", EditorStyles.boldLabel);

            if (cachedPoolInfo.Count == 0)
            {
                EditorGUILayout.HelpBox("No pools active. Pools are created on first use.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Type",  EditorStyles.miniLabel, GUILayout.MinWidth(180));
            EditorGUILayout.LabelField("Size",  EditorStyles.miniLabel, GUILayout.Width(50));
            EditorGUILayout.LabelField("Max",   EditorStyles.miniLabel, GUILayout.Width(50));
            EditorGUILayout.LabelField("Usage", EditorStyles.miniLabel, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            foreach (var (type, size, maxSize) in cachedPoolInfo.OrderByDescending(p => p.size))
            {
                EditorGUILayout.BeginHorizontal();

                var name = type.IsGenericType
                    ? type.Name.Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(a => a.Name)) + ">"
                    : type.Name;

                EditorGUILayout.LabelField(name, GUILayout.MinWidth(180));
                EditorGUILayout.LabelField(size.ToString(),    GUILayout.Width(50));
                EditorGUILayout.LabelField(maxSize.ToString(), GUILayout.Width(50));

                var ratio = maxSize > 0 ? (float)size / maxSize : 0f;
                var rect  = EditorGUILayout.GetControlRect(GUILayout.Width(60));
                EditorGUI.ProgressBar(rect, ratio, $"{ratio:P0}");

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"Total pools: {cachedPoolInfo.Count}", EditorStyles.miniLabel);
        }

        void DrawConfig()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Runtime Configuration", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            EditorGUILayout.LabelField("DefaultMaxPoolSize",  ValkarnTask.DefaultMaxPoolSize.ToString());
            EditorGUILayout.LabelField("TrimCheckInterval",   ValkarnTask.TrimCheckInterval.ToString() + " frames");
            EditorGUILayout.LabelField("MinPoolSize",         ValkarnTask.MinPoolSize.ToString());
            EditorGUI.EndDisabledGroup();

            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Enter Play Mode to see live configuration values.", MessageType.Info);

            EditorGUILayout.Space(4);
            var settings = ValkarnTaskSettings.Instance;
            if (settings != null)
                EditorGUILayout.ObjectField("Settings Asset", settings, typeof(ValkarnTaskSettings), false);
            else
                EditorGUILayout.HelpBox(
                    "No ValkarnTaskSettings asset found in Resources. " +
                    "Create one via Assets > Create > Valkarn > Tasks > Task Settings " +
                    "and place it in a Resources folder.",
                    MessageType.Warning);
        }
    }
}
#endif
