namespace WinStart.Core.Unattend;

public enum ImageCheckKind { Ok, Warn, Info }

public sealed record ImageCheckResult(ImageCheckKind Kind, string Key, params object[] Args);

public static class ImageCheck
{
    public static IReadOnlyList<ImageCheckResult> Compare(UnattendSettings s, IReadOnlyList<WindowsImageInfo> images)
    {
        var lines = new List<ImageCheckResult>();
        if (images.Count == 0)
        {
            lines.Add(new(ImageCheckKind.Warn, "ua.check.noWim"));
            return lines;
        }

        foreach (var i in images)
            lines.Add(new(ImageCheckKind.Info, "ua.check.entry", i.Index, i.Name, i.Architecture, i.Build,
                i.Languages.Count == 0 ? "?" : string.Join(", ", i.Languages)));

        Language(s, images, lines);
        Edition(s, images, lines);
        Architecture(s, images, lines);

        if (images.All(i => i.Build > 0 && i.Build < 22000))
            lines.Add(new(ImageCheckKind.Info, "ua.check.win10"));

        return lines;
    }

    private static void Language(UnattendSettings s, IReadOnlyList<WindowsImageInfo> images, List<ImageCheckResult> lines)
    {
        if (s.LanguageInteractive)
        {
            lines.Add(new(ImageCheckKind.Info, "ua.check.langAsk"));
            return;
        }

        var lang = s.DisplayLanguage;
        var with = images.Where(i => i.Languages.Contains(lang, StringComparer.OrdinalIgnoreCase)).ToList();
        if (with.Count == images.Count)
            lines.Add(new(ImageCheckKind.Ok, "ua.check.langOk", lang));
        else if (with.Count > 0)
            lines.Add(new(ImageCheckKind.Warn, "ua.check.langSome", lang, string.Join(", ", with.Select(i => i.Index))));
        else
        {
            var available = images.SelectMany(i => i.Languages).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            lines.Add(new(ImageCheckKind.Warn, "ua.check.langMissing", lang,
                available.Count == 0 ? "?" : string.Join(", ", available)));
        }
    }

    private static readonly Dictionary<string, string> EditionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = "Core",
        ["home_n"] = "CoreN",
        ["home_single"] = "CoreSingleLanguage",
        ["education"] = "Education",
        ["education_n"] = "EducationN",
        ["pro"] = "Professional",
        ["pro_n"] = "ProfessionalN",
        ["pro_education"] = "ProfessionalEducation",
        ["pro_education_n"] = "ProfessionalEducationN",
        ["pro_workstations"] = "ProfessionalWorkstation",
        ["pro_workstations_n"] = "ProfessionalWorkstationN",
        ["enterprise"] = "Enterprise",
        ["enterprise_n"] = "EnterpriseN"
    };

    private static void Edition(UnattendSettings s, IReadOnlyList<WindowsImageInfo> images, List<ImageCheckResult> lines)
    {
        var names = string.Join(", ", images.Select(i => i.Name));
        var family = images[0].Family;
        var checkedSomething = false;

        void ByEdition(string id, bool selectsByName)
        {
            checkedSomething = true;
            var title = UnattendCatalog.Editions.FirstOrDefault(e => e.Value == id)?.Title ?? id;
            var expectedName = $"{family} {title}";
            var editionId = EditionIds.GetValueOrDefault(id, id);

            var hit = images.FirstOrDefault(i => string.Equals(i.Edition, editionId, StringComparison.OrdinalIgnoreCase))
                      ?? images.FirstOrDefault(i => string.Equals(i.Name, expectedName, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
            {
                lines.Add(new(ImageCheckKind.Warn, "ua.check.editionMissing", expectedName, names));
                return;
            }

            lines.Add(new(ImageCheckKind.Ok, "ua.check.editionOk", hit.Name));

            if (selectsByName && !string.Equals(hit.Name, expectedName, StringComparison.OrdinalIgnoreCase))
                lines.Add(new(ImageCheckKind.Warn, "ua.check.nameDiffers", expectedName, hit.Name, hit.Index));
        }

        if (s.EditionMode == EditionMode.Generic) ByEdition(s.GenericEdition, selectsByName: false);

        if (s.PeMode == PeMode.Generate)
        {
            switch (s.ImageMode)
            {
                case ImageMode.Edition:
                    ByEdition(s.ImageEdition, selectsByName: true);
                    break;

                case ImageMode.Name:
                    checkedSomething = true;
                    var name = s.ImageName.Trim();
                    lines.Add(images.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
                        ? new(ImageCheckKind.Ok, "ua.check.editionOk", name)
                        : new(ImageCheckKind.Warn, "ua.check.editionMissing", name, names));
                    break;

                case ImageMode.Index:
                    checkedSomething = true;
                    var hit = images.FirstOrDefault(i => i.Index == s.ImageIndex);
                    lines.Add(hit is not null
                        ? new(ImageCheckKind.Ok, "ua.check.indexOk", s.ImageIndex, hit.Name)
                        : new(ImageCheckKind.Warn, "ua.check.indexMissing", s.ImageIndex, images.Count));
                    break;
            }
        }

        if (!checkedSomething) lines.Add(new(ImageCheckKind.Info, "ua.check.editionAsk"));
    }

    private static void Architecture(UnattendSettings s, IReadOnlyList<WindowsImageInfo> images, List<ImageCheckResult> lines)
    {
        var selected = new List<string>();
        if (s.ArchX86) selected.Add("x86");
        if (s.ArchAmd64) selected.Add("amd64");
        if (s.ArchArm64) selected.Add("arm64");

        var archs = images.Select(i => i.Architecture).Where(a => a.Length > 0).Distinct().ToList();
        var missing = archs.Where(a => !selected.Contains(a)).ToList();

        if (archs.Count == 0) return;
        if (missing.Count == 0)
            lines.Add(new(ImageCheckKind.Ok, "ua.check.archOk", string.Join(", ", archs)));
        else
            lines.Add(new(ImageCheckKind.Warn, "ua.check.archMismatch", string.Join(", ", missing), string.Join(", ", selected)));
    }
}
