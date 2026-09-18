using System;

namespace TNovViewsSheets
{
    internal static class ChangesParams
    {
        public static readonly Guid AdskComment = new Guid("a85b7661-26b0-412f-979c-66af80b4b2c3"); // ADSK_Примечание
        public static readonly Guid Comment2 = new Guid("7243f857-6292-45a1-8727-26ea5b09f450"); // Примечание
        public static readonly Guid SheetSet = new Guid("e1b06433-f527-403c-8986-af9a01e6be7f"); // A_Комплект чертежей
        public static readonly Guid CustomSheetNumber = new Guid("b6e73342-b6cd-42c5-86c5-64b04b5b88de"); // N_Ш.НомерЛиста

        public const string SheetGroupParamName = "Изм.Группа листов";
        public const string SheetContentParamName = "Изм.Содержание изменения";

        public const double MinCloudLength = 0.03;
        public const string NoSetName = "без комплекта";
        public const string AllSetsName = "Все";

        public static readonly Guid[] CountLineGuids =
        {
            new Guid("d37ea57b-0808-4d6d-92a1-7fc6227d3f18"), new Guid("688059f1-20a0-491b-b704-9e7735963a11"),
            new Guid("e82994ec-1686-4bc9-be42-1a3edc2dc6eb"), new Guid("342ab652-cb9c-4ca9-be4f-3be323f1fcea"),
            new Guid("5682ac53-4a96-4b99-b4c3-8b378d353ebd"), new Guid("7833ad6e-e746-47ea-8155-85bff1f819bf"),
            new Guid("b2c74713-128c-4df7-982d-bcdb941afb5c"), new Guid("8e092f0d-c607-4bdb-b73a-bcf71982f5d7"),
            new Guid("3fb4479d-84d7-429e-bd0f-7e6152ac9b0a"), new Guid("e57d297d-b0a7-4633-b4e9-c0e205b37db7"),
            new Guid("11776b7a-e30a-411d-ae48-f65ac87d4ef7"), new Guid("d2c00464-8a76-4234-a6cc-2e6767e8f64c"),
            new Guid("680d7933-4f58-4324-9eee-01675958fbd8"), new Guid("106e190a-4365-464a-b434-9587dde6fa03")
        };

        public static readonly Guid[] SheetLineGuids =
        {
            new Guid("8b5eb639-5e9d-4597-aab5-930c3d919674"), new Guid("c8d0f5a1-4617-4cd4-932c-4c93f35bb213"),
            new Guid("140ea739-47c9-40bc-9b07-3b4d862eae99"), new Guid("76ad2603-00a9-4467-959c-4d3485539f7e"),
            new Guid("8967e4fa-3cce-42e9-a5a9-f6df001476dc"), new Guid("3bb18b6d-e381-4962-b8f7-9f64be8ec998"),
            new Guid("5c9388c8-ae8c-420e-9011-0e2c35219592"), new Guid("6830df72-f844-4edb-8cd1-c854fccc623c"),
            new Guid("d270aba5-f903-4dcb-87a1-49fb17f4c5a3"), new Guid("d7303fe2-677c-42cd-b378-409c75137193"),
            new Guid("4fdd5ee2-5ca9-4bd7-b7a4-7102084e1262"), new Guid("8ff1fd64-b4db-4549-8cdb-bdc8693e640e"),
            new Guid("d550c5c9-777e-4bbd-b394-79a85161eca8"), new Guid("7f13f8e5-ad02-486d-b4d1-1ade7a2d2e5f")
        };
    }

    public enum ChangesScope
    {
        All,
        Visible,
        Selected
    }

    public enum ChangeStampKind
    {
        Partial,
        Replace,
        New
    }
}
