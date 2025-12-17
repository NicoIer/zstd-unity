// Copyright (c) 2025 NicoIer and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

#if UNITY_5_3_OR_NEWER

using TMPro;
using UnityEngine;

namespace zstd.samples
{
    public class zstd_test : MonoBehaviour
    {
        [SerializeField] public TextMeshProUGUI versionText;

        void Start()
        {
            versionText.text = "zstd version: " + ZStandardAPI.versionString;
        }
    }
}

#endif