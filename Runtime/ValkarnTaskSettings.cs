// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace UnaPartidaMas.Valkarn.Tasks
{
#if UNITY_5_3_OR_NEWER
    [CreateAssetMenu(menuName = "Valkarn/Tasks/Task Settings", fileName = "ValkarnTaskSettings")]
    public sealed class ValkarnTaskSettings : ScriptableObject
    {
        static ValkarnTaskSettings s_instance;

        public static ValkarnTaskSettings Instance
        {
            get
            {
                if (s_instance == null)
                    s_instance = Resources.Load<ValkarnTaskSettings>("ValkarnTaskSettings");
                return s_instance;
            }
        }

        internal static void ResetInstance() { s_instance = null; }

        [Header("Pool Configuration")]
        [Tooltip("Maximum items per pool type. Excess items are trimmed.")]
        [SerializeField, Range(8, 1024)]
        int defaultMaxPoolSize = 256;

        [Tooltip("Minimum pool size — never shrink below this.")]
        [SerializeField, Range(1, 64)]
        int minPoolSize = 8;

        [Tooltip("Frames between trim checks. At 60fps, 300 ≈ 5 seconds.")]
        [SerializeField, Range(30, 1000)]
        int trimCheckInterval = 300;

        [Tooltip("Number of consecutive above-threshold checks before trimming.")]
        [SerializeField, Range(1, 10)]
        int trimHysteresisCount = 2;

        [Tooltip("Fraction of excess to release per trim cycle (0.25 = 25%).")]
        [SerializeField, Range(0.1f, 1.0f)]
        float trimReleaseRatio = 0.25f;

        [Header("Lifecycle")]
        [Tooltip("Auto-bind MonoBehaviour tasks to destroyCancellationToken.")]
        [SerializeField]
        bool enableAutoCancel = true;

        [Header("Error Handling")]
        [Tooltip("Log unobserved cancellations as warnings.")]
        [SerializeField]
        bool logUnobservedCancellations;

        [Tooltip("Maximum exception logs per frame to prevent spam.")]
        [SerializeField, Range(1, 100)]
        int maxExceptionLogsPerFrame = 10;

        // Public properties
        public int DefaultMaxPoolSize => defaultMaxPoolSize;
        public int MinPoolSize => minPoolSize;
        public int TrimCheckInterval => trimCheckInterval;
        public int TrimHysteresisCount => trimHysteresisCount;
        public float TrimReleaseRatio => trimReleaseRatio;
        public bool EnableAutoCancel => enableAutoCancel;
        public bool LogUnobservedCancellations => logUnobservedCancellations;
        public int MaxExceptionLogsPerFrame => maxExceptionLogsPerFrame;
    }
#else
    /// <summary>
    /// Non-Unity fallback: static configuration used in tests and standalone .NET builds.
    /// </summary>
    public static class ValkarnTaskSettings
    {
        public static int DefaultMaxPoolSize { get; set; } = 256;
        public static int MinPoolSize { get; set; } = 8;
        public static int TrimCheckInterval { get; set; } = 300;
        public static int TrimHysteresisCount { get; set; } = 2;
        public static float TrimReleaseRatio { get; set; } = 0.25f;
        public static bool EnableAutoCancel { get; set; } = true;
        public static bool LogUnobservedCancellations { get; set; }
        public static int MaxExceptionLogsPerFrame { get; set; } = 10;
    }
#endif
}
