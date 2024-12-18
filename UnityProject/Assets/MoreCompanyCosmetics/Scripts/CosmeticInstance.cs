using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MoreCompany.Cosmetics
{
    public class CosmeticInstance : MonoBehaviour
    {
        public CosmeticType cosmeticType;
        public string cosmeticId;
        public Texture2D icon;
    }

    public enum CosmeticType
    {
        HAT,
        WRIST,
        CHEST,
        R_LOWER_ARM,
        HIP,
        L_SHIN,
        R_SHIN
    }
}

