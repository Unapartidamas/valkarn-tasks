// Copyright (c) 2026 Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnaPartidaMas.Valkarn.Editor
{
    /// <summary>
    /// Central hub for all Valkarn tools. Open via Tools > Valkarn > Hub.
    ///
    /// Panels from any installed Valkarn package appear automatically —
    /// no manual registration needed. Each package implements
    /// <see cref="IValkarnEditorPanel"/> and TypeCache does the rest.
    /// </summary>
    public sealed class ValkarnHubWindow : EditorWindow
    {
        // ── Known Valkarn packages (for the Discover section) ──────────────

        static readonly KnownPackage[] KnownPackages =
        {
            new("com.unapartidamas.valkarn.tasks",
                "Valkarn Tasks",
                "Zero-allocation async/await framework. Struct tasks, IL2CPP-optimized, auto-cancel, channels.",
                "https://tasks.valkarn.com"),
        };

        // ───────────────────────────────────────────────────────────────────

        const float SidebarWidth  = 160f;
        const float HeaderHeight  = 48f;
        const float FooterHeight  = 24f;

        List<IValkarnEditorPanel> panels;
        int selectedIndex;
        Vector2 sidebarScroll;
        Vector2 contentScroll;

        GUIStyle headerStyle;
        GUIStyle sidebarButtonStyle;
        GUIStyle sidebarButtonActiveStyle;
        GUIStyle versionStyle;
        bool stylesReady;

        [MenuItem("Tools/Valkarn/Hub", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<ValkarnHubWindow>();
            window.titleContent = new GUIContent("Valkarn Hub");
            window.minSize = new Vector2(560, 320);
            window.Show();
        }

        void OnEnable()
        {
            RefreshPanels();
        }

        void OnDisable()
        {
            panels?[selectedIndex]?.OnDisable();
        }

        void RefreshPanels()
        {
            var prev = panels != null && panels.Count > selectedIndex
                ? panels[selectedIndex]?.PackageId
                : null;

            panels?.ElementAtOrDefault(selectedIndex)?.OnDisable();
            panels = DiscoverPanels();

            selectedIndex = 0;
            if (prev != null)
            {
                var idx = panels.FindIndex(p => p.PackageId == prev);
                if (idx >= 0) selectedIndex = idx;
            }

            panels.ElementAtOrDefault(selectedIndex)?.OnEnable();
        }

        static List<IValkarnEditorPanel> DiscoverPanels()
        {
            var result = new List<IValkarnEditorPanel>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IValkarnEditorPanel>())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                try
                {
                    if (Activator.CreateInstance(type) is IValkarnEditorPanel panel)
                        result.Add(panel);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ValkarnHub] Could not instantiate panel {type.FullName}: {e.Message}");
                }
            }
            return result.OrderBy(p => p.Order).ThenBy(p => p.Title).ToList();
        }

        void OnGUI()
        {
            EnsureStyles();

            var fullRect = new Rect(0, 0, position.width, position.height);

            DrawHeader(new Rect(0, 0, position.width, HeaderHeight));

            var bodyRect = new Rect(0, HeaderHeight, position.width, position.height - HeaderHeight - FooterHeight);
            DrawBody(bodyRect);

            DrawFooter(new Rect(0, position.height - FooterHeight, position.width, FooterHeight));
        }

        void DrawHeader(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
            GUI.Label(new Rect(rect.x + 12, rect.y + 10, rect.width - 80, 28), "Valkarn", headerStyle);
            GUI.Label(new Rect(rect.xMax - 140, rect.yMax - 16, 136, 14), "unapartidamas.com", versionStyle);
        }

        void DrawBody(Rect rect)
        {
            // Sidebar
            var sidebarRect = new Rect(rect.x, rect.y, SidebarWidth, rect.height);
            DrawSidebar(sidebarRect);

            // Divider
            EditorGUI.DrawRect(new Rect(sidebarRect.xMax, rect.y, 1, rect.height), new Color(0.1f, 0.1f, 0.1f));

            // Content
            var contentRect = new Rect(sidebarRect.xMax + 1, rect.y, rect.width - SidebarWidth - 1, rect.height);
            DrawContent(contentRect);
        }

        void DrawSidebar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));

            var viewRect = new Rect(rect.x, rect.y, rect.width - 14, panels.Count * 28 + 8);
            sidebarScroll = GUI.BeginScrollView(rect, sidebarScroll, viewRect, false, false,
                GUIStyle.none, GUIStyle.none);

            float y = rect.y + 4;
            for (int i = 0; i < panels.Count; i++)
            {
                var btnRect = new Rect(rect.x + 4, y, rect.width - 8, 24);
                var style = i == selectedIndex ? sidebarButtonActiveStyle : sidebarButtonStyle;

                if (GUI.Button(btnRect, panels[i].Title, style))
                    SelectPanel(i);

                y += 28;
            }

            GUI.EndScrollView();
        }

        void DrawContent(Rect rect)
        {
            if (panels.Count == 0)
            {
                DrawDiscover(rect);
                return;
            }

            GUILayout.BeginArea(rect);
            contentScroll = EditorGUILayout.BeginScrollView(contentScroll);

            try
            {
                panels[selectedIndex].OnGUI();
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox($"Panel error: {e.Message}", MessageType.Error);
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawDiscover(Rect rect)
        {
            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 16, rect.width - 32, rect.height - 32));
            EditorGUILayout.LabelField("No Valkarn panels found.", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Install a Valkarn package to get started.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(12);
            DrawDiscoverPackages();
            GUILayout.EndArea();
        }

        void DrawDiscoverPackages()
        {
            var installedIds = new HashSet<string>(panels.Select(p => p.PackageId));

            foreach (var pkg in KnownPackages)
            {
                if (installedIds.Contains(pkg.Id)) continue;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(pkg.DisplayName, EditorStyles.boldLabel);

                    EditorGUILayout.LabelField(pkg.Description, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(2);

                    if (GUILayout.Button("Ver más ↗", GUILayout.Width(80)))
                        Application.OpenURL(pkg.Url);
                }
                EditorGUILayout.Space(4);
            }
        }

        void DrawFooter(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));

            if (panels.Count > 0)
            {
                var pkg = KnownPackages.FirstOrDefault(k => k.Id == panels[selectedIndex].PackageId);
                if (pkg.Id != null)
                {
                    var btnRect = new Rect(rect.xMax - 64, rect.y + 4, 60, 16);
                    if (GUI.Button(btnRect, "Docs ↗", EditorStyles.toolbarButton))
                        Application.OpenURL(pkg.Url);
                }
            }
        }

        void SelectPanel(int index)
        {
            if (index == selectedIndex) return;
            panels[selectedIndex]?.OnDisable();
            selectedIndex = index;
            panels[selectedIndex]?.OnEnable();
            contentScroll = Vector2.zero;
            Repaint();
        }

        void EnsureStyles()
        {
            if (stylesReady) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 20,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = Color.white }
            };

            versionStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };

            sidebarButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize  = 12,
                padding   = new RectOffset(8, 4, 2, 2),
                normal    = { background = null, textColor = new Color(0.75f, 0.75f, 0.75f) },
                hover     = { background = null, textColor = Color.white },
                active    = { background = null, textColor = Color.white },
            };

            sidebarButtonActiveStyle = new GUIStyle(sidebarButtonStyle)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white }
            };

            stylesReady = true;
        }

        // ── Data ────────────────────────────────────────────────────────────

        readonly struct KnownPackage
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string Description;
            public readonly string Url;

            public KnownPackage(string id, string displayName, string description, string url)
            {
                Id          = id;
                DisplayName = displayName;
                Description = description;
                Url         = url;
            }
        }
    }
}
#endif
