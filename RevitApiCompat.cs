using Autodesk.Revit.DB;

namespace TNovViewsSheets
{
    internal static class RevitApiCompat
    {
        public static int ElementIdIntValue(ElementId elementId)
        {
#if R2022
            return elementId.IntegerValue;
#else
            return checked((int)elementId.Value);
#endif
        }

        public static ElementId CreateElementId(int value)
        {
#if R2022
            return new ElementId(value);
#else
            return new ElementId((long)value);
#endif
        }

        /// <summary>
        /// Родительский экземпляр семейства (не вложенный в другой shared-экземпляр).
        /// </summary>
        public static bool IsTopLevelFamilyInstance(FamilyInstance instance)
        {
#if R2022
            Element superComponent = instance.SuperComponent;
            return superComponent == null;
#else
            return instance.SuperComponent == null;
#endif
        }
    }
}
