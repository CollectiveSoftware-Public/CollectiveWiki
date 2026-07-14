// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Wiki.Core.Search;
using Xunit;

namespace Wiki.Core.Tests;

public class SearchQueryTests
{
    [Fact] public void Parses_plain_terms()
        => Assert.Equal(new[] { "alpha", "beta" }, SearchQuery.Parse("alpha beta").Terms.ToArray());

    [Fact] public void Parses_tag_operator()
        => Assert.Equal(new[] { "project" }, SearchQuery.Parse("tag:project").Tags.ToArray());

    [Fact] public void Strips_leading_hash_on_tag()
        => Assert.Equal(new[] { "project" }, SearchQuery.Parse("tag:#project").Tags.ToArray());

    [Fact] public void Parses_path_operator()
        => Assert.Equal(new[] { "journal" }, SearchQuery.Parse("path:journal").Paths.ToArray());

    [Fact] public void Parses_quoted_phrase_with_spaces()
        => Assert.Equal(new[] { "exact match" }, SearchQuery.Parse("\"exact match\"").Phrases.ToArray());

    [Fact] public void Parses_mixed_query()
    {
        var q = SearchQuery.Parse("tag:kb path:notes \"hello world\" loose");
        Assert.Equal(new[] { "kb" }, q.Tags.ToArray());
        Assert.Equal(new[] { "notes" }, q.Paths.ToArray());
        Assert.Equal(new[] { "hello world" }, q.Phrases.ToArray());
        Assert.Equal(new[] { "loose" }, q.Terms.ToArray());
        Assert.True(q.HasFilters);
    }

    [Fact] public void Unknown_prefix_is_a_plain_term()
        => Assert.Equal(new[] { "foo:bar" }, SearchQuery.Parse("foo:bar").Terms.ToArray());
}
