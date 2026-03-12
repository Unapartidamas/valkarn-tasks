// Copyright (c) 2026 Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if UNITY_EDITOR
namespace UnaPartidaMas.Valkarn.Editor
{
    /// <summary>
    /// Implement this interface in any Editor assembly to register a panel
    /// in the Valkarn Hub (Tools > Valkarn > Hub).
    ///
    /// The Hub discovers all implementations automatically via TypeCache —
    /// no registration required. Works across packages.
    /// </summary>
    public interface IValkarnEditorPanel
    {
        /// <summary>Display name shown in the sidebar tab.</summary>
        string Title { get; }

        /// <summary>
        /// UPM package id this panel belongs to (e.g. "com.unapartidamas.valkarn.tasks").
        /// Used for ordering and deduplication.
        /// </summary>
        string PackageId { get; }

        /// <summary>Sidebar sort order. Lower values appear first.</summary>
        int Order { get; }

        /// <summary>Called once when the panel is first selected.</summary>
        void OnEnable() { }

        /// <summary>Called when the panel is deselected or the window closes.</summary>
        void OnDisable() { }

        /// <summary>Draw the panel content. Called every repaint while selected.</summary>
        void OnGUI();
    }
}
#endif
