using System.Collections.Generic;

namespace Raphael.Services.Guides;

// 0.73.0: the guide registry, in the order the Settings and Help tabs list them. Each guide lives in its own file and
// implements one of these partial methods (an unimplemented one compiles away, so a guide can be added on its own).
internal static partial class Guides
{
    static partial void AddQuickStart(List<Guide> list);
    static partial void AddBloodcraft(List<Guide> list);
    static partial void AddBeelzebub(List<Guide> list);
    static partial void AddUriel(List<Guide> list);
    static partial void AddFaust(List<Guide> list);
    static partial void AddNyarlathotep(List<Guide> list);
    static partial void AddKindred(List<Guide> list);
    static partial void AddVRisingBasics(List<Guide> list);
    static partial void AddGameGuide(List<Guide> list);

    private static Guide[] BuildAll()
    {
        var list = new List<Guide>();
        AddQuickStart(list);
        AddBloodcraft(list);
        AddBeelzebub(list);
        AddUriel(list);
        AddFaust(list);
        AddNyarlathotep(list);
        AddKindred(list);
        AddVRisingBasics(list);
        AddGameGuide(list);
        return list.ToArray();
    }
}
