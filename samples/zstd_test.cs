// Copyright (c) 2025 NicoIer and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

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