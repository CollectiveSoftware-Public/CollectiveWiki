// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Core.Templating;

/// <summary>The values a template can interpolate: the note title and the resolved instant
/// (supplied by the caller from an <c>IClock</c>, keeping the engine pure).</summary>
public sealed record TemplateContext(string Title, DateTimeOffset Now);
