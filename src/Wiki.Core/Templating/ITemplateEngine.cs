// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Templating;

/// <summary>Renders a template string by replacing <c>{{title}}</c>, <c>{{date}}</c>, <c>{{time}}</c>
/// and <c>{{date:FORMAT}}</c> / <c>{{time:FORMAT}}</c> placeholders. Unknown placeholders pass through.</summary>
public interface ITemplateEngine
{
    string Render(string template, TemplateContext context);
}
