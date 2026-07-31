using QuestPDF.Infrastructure;

namespace Retail25.Infrastructure.Documents;

/// <summary>
/// QuestPDF refuses to generate until the licence has been accepted in code, and it throws at
/// <em>render</em> time rather than at startup.
/// <para>
/// Accepting it here, from the static constructor of every renderer, ties it to the thing that needs
/// it. Doing it in the DI registration instead means any path that constructs a renderer directly —
/// a unit test, a console tool, a background job wired by hand — throws on the first PDF, which
/// surfaces to an operator as a print button that does nothing.
/// </para>
/// </summary>
internal static class QuestPdfLicence
{
    private static readonly object Gate = new();
    private static bool _accepted;

    public static void Accept()
    {
        if (_accepted)
        {
            return;
        }

        lock (Gate)
        {
            if (_accepted)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;
            _accepted = true;
        }
    }
}
