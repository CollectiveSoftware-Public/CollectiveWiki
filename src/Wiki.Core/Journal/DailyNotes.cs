// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Templating;
using Wiki.Core.Time;
using Wiki.Core.Vault;

namespace Wiki.Core.Journal;

/// <summary>The default daily-notes service. Pure but for the injected <see cref="IClock"/> and
/// <see cref="IVaultFileSystem"/> — both seams, so it is fully testable headlessly.</summary>
public sealed class DailyNotes : IDailyNotes
{
    private readonly IVaultFileSystem _files;
    private readonly IClock _clock;
    private readonly ITemplateEngine _templates;
    private readonly DailyNoteOptions _options;

    public DailyNotes(IVaultFileSystem files, IClock clock, ITemplateEngine templates, DailyNoteOptions options)
        => (_files, _clock, _templates, _options) = (files, clock, templates, options);

    public string ResolvePath(DateTimeOffset date)
    {
        string name = date.ToString(_options.DateFormat) + ".md";
        return string.IsNullOrEmpty(_options.Folder) ? name : _options.Folder.TrimEnd('/') + "/" + name;
    }

    public string GetOrCreateToday()
    {
        var now = _clock.Now;
        string path = ResolvePath(now);
        if (_files.Exists(path)) return path;

        string title = now.ToString(_options.DateFormat);
        string content = "";
        if (_options.TemplatePath is { } tpl && _files.Exists(tpl))
            content = _templates.Render(_files.ReadAllText(tpl), new TemplateContext(title, now));

        _files.WriteAllText(path, content);
        return path;
    }
}
