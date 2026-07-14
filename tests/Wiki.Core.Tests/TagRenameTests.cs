// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Indexing;
using Xunit;

namespace Wiki.Core.Tests;

public class TagRenameTests
{
    [Fact] public void Renames_the_exact_tag()
        => Assert.Equal("see #done here", TagRename.Rewrite("see #todo here", "todo", "done"));

    [Fact] public void Renames_nested_children_preserving_the_suffix()
        => Assert.Equal("#work/urgent", TagRename.Rewrite("#area/urgent", "area", "work"));

    [Fact] public void Leaves_tags_inside_inline_code_untouched()
        => Assert.Equal("`#todo` then #done",
            TagRename.Rewrite("`#todo` then #todo", "todo", "done"));

    [Fact] public void No_op_when_absent()
        => Assert.Equal("nothing here", TagRename.Rewrite("nothing here", "todo", "done"));
}
