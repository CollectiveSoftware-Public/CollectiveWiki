// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Desktop.ViewModels;
using Xunit;

namespace Wiki.Desktop.Tests;

public class OpenTabsViewModelTests
{
    [Fact]
    public void Opening_same_note_twice_focuses_one_tab()
    {
        var vm = new OpenTabsViewModel();
        var a = vm.OpenNote("a.md", "a", activate: true);
        var b = vm.OpenNote("a.md", "a", activate: true);
        Assert.Same(a, b);
        Assert.Single(vm.Tabs);
        Assert.Same(a, vm.Active);
    }

    [Fact]
    public void Opening_an_image_adds_an_image_tab()
    {
        var vm = new OpenTabsViewModel();
        var t = vm.OpenImage(@"C:\v\x.png", "x.png", activate: true);
        Assert.Equal(TabKind.Image, t.Kind);
        Assert.Contains(t, vm.Tabs);
        Assert.Same(t, vm.Active);
    }

    [Fact]
    public void Closing_the_active_middle_tab_activates_the_neighbour()
    {
        var vm = new OpenTabsViewModel();
        vm.OpenNote("a.md", "a", true);
        var b = vm.OpenNote("b.md", "b", true);
        var c = vm.OpenNote("c.md", "c", true);
        vm.Active = b;
        vm.Close(b);
        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(c, vm.Active);
    }

    [Fact]
    public void Closing_the_last_tab_clears_active()
    {
        var vm = new OpenTabsViewModel();
        var a = vm.OpenNote("a.md", "a", true);
        vm.Close(a);
        Assert.Empty(vm.Tabs);
        Assert.Null(vm.Active);
    }

    [Fact]
    public void First_opened_tab_becomes_active_even_without_activate()
    {
        var vm = new OpenTabsViewModel();
        var a = vm.OpenNote("a.md", "a", activate: false);
        Assert.Same(a, vm.Active);   // nothing was active, so the first tab activates
    }

    [Fact]
    public void NavigateActive_reuses_the_active_tab_slot()
    {
        var vm = new OpenTabsViewModel();
        var a = vm.OpenNote("a.md", "a", true);
        var nav = vm.NavigateActive("b.md", "b");
        Assert.Single(vm.Tabs);                 // no new tab — the slot was reused
        Assert.Same(nav, vm.Active);
        Assert.Equal("note:b.md", vm.Active!.Key);
        Assert.DoesNotContain(a, vm.Tabs);      // the old tab was dropped
    }

    [Fact]
    public void NavigateActive_keeps_the_position_of_the_reused_tab()
    {
        var vm = new OpenTabsViewModel();
        vm.OpenNote("a.md", "a", true);
        var b = vm.OpenNote("b.md", "b", true);
        vm.OpenNote("c.md", "c", true);
        vm.Active = b;
        vm.NavigateActive("z.md", "z");
        Assert.Equal(3, vm.Tabs.Count);
        Assert.Equal("note:z.md", vm.Tabs[1].Key);   // replaced b in the middle slot
        Assert.Same(vm.Tabs[1], vm.Active);
    }

    [Fact]
    public void NavigateActive_to_an_already_open_note_focuses_it_without_duplicating()
    {
        var vm = new OpenTabsViewModel();
        var a = vm.OpenNote("a.md", "a", true);
        var b = vm.OpenNote("b.md", "b", true);   // b active
        var nav = vm.NavigateActive("a.md", "a");
        Assert.Same(a, nav);                       // focused the existing a tab
        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(a, vm.Active);
        Assert.Contains(b, vm.Tabs);               // b untouched
    }

    [Fact]
    public void NavigateActive_with_no_active_tab_opens_a_new_one()
    {
        var vm = new OpenTabsViewModel();
        var nav = vm.NavigateActive("a.md", "a");
        Assert.Single(vm.Tabs);
        Assert.Same(nav, vm.Active);
    }

    [Fact]
    public void NavigateActive_does_not_reuse_an_image_tab()
    {
        var vm = new OpenTabsViewModel();
        var img = vm.OpenImage(@"C:\v\x.png", "x.png", true);
        vm.NavigateActive("a.md", "a");
        Assert.Equal(2, vm.Tabs.Count);            // image tab preserved, note opened alongside
        Assert.Contains(img, vm.Tabs);
        Assert.Equal("note:a.md", vm.Active!.Key);
    }

    // ---- guarded close (Task 1): CloseAsync consults the veto, Close never does ----

    [Fact]
    public async Task CloseAsync_with_a_veto_returning_false_keeps_the_tab_and_active_state()
    {
        var vm = new OpenTabsViewModel();
        vm.OpenNote("a.md", "a", true);
        var b = vm.OpenNote("b.md", "b", true);   // b active
        vm.ClosingAsync = _ => Task.FromResult(false);

        bool closed = await vm.CloseAsync(b);

        Assert.False(closed);
        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(b, vm.Active);                // active state preserved
    }

    [Fact]
    public async Task CloseAsync_with_a_veto_returning_true_removes_and_activates_the_neighbour()
    {
        var vm = new OpenTabsViewModel();
        vm.OpenNote("a.md", "a", true);
        var b = vm.OpenNote("b.md", "b", true);
        var c = vm.OpenNote("c.md", "c", true);
        vm.Active = b;
        vm.ClosingAsync = _ => Task.FromResult(true);

        bool closed = await vm.CloseAsync(b);

        Assert.True(closed);
        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(c, vm.Active);                // next-else-previous neighbour
        Assert.DoesNotContain(b, vm.Tabs);
    }

    [Fact]
    public async Task CloseAsync_with_no_veto_closes()
    {
        var vm = new OpenTabsViewModel();
        var a = vm.OpenNote("a.md", "a", true);

        bool closed = await vm.CloseAsync(a);

        Assert.True(closed);
        Assert.Empty(vm.Tabs);
        Assert.Null(vm.Active);
    }

    [Fact]
    public async Task Sync_Close_never_consults_the_veto()
    {
        var vm = new OpenTabsViewModel();
        var a = vm.OpenNote("a.md", "a", true);
        bool vetoConsulted = false;
        vm.ClosingAsync = _ => { vetoConsulted = true; return Task.FromResult(false); };

        vm.Close(a);
        await Task.CompletedTask;

        Assert.False(vetoConsulted);              // Close bypasses the veto entirely
        Assert.Empty(vm.Tabs);
    }

    // ---- scoped bulk closes (tab right-click menu): Close Others / to the Left / to the Right / All ----

    [Fact]
    public async Task CloseOthersAsync_closes_all_but_the_anchor()
    {
        var vm = new OpenTabsViewModel();
        vm.OpenNote("a.md", "a", true);
        var b = vm.OpenNote("b.md", "b", true);
        vm.OpenNote("c.md", "c", true);

        await vm.CloseOthersAsync(b);

        Assert.Single(vm.Tabs);
        Assert.Same(b, vm.Tabs[0]);
        Assert.Same(b, vm.Active);
    }

    [Fact]
    public async Task CloseToLeftAsync_closes_only_tabs_before_the_anchor()
    {
        var vm = new OpenTabsViewModel();
        vm.OpenNote("a.md", "a", true);
        vm.OpenNote("b.md", "b", true);
        var c = vm.OpenNote("c.md", "c", true);
        var d = vm.OpenNote("d.md", "d", true);

        await vm.CloseToLeftAsync(c);

        Assert.Equal(new[] { "note:c.md", "note:d.md" }, vm.Tabs.Select(t => t.Key));
        Assert.Contains(d, vm.Tabs);
    }

    [Fact]
    public async Task CloseToRightAsync_closes_only_tabs_after_the_anchor()
    {
        var vm = new OpenTabsViewModel();
        var a = vm.OpenNote("a.md", "a", true);
        var b = vm.OpenNote("b.md", "b", true);
        vm.OpenNote("c.md", "c", true);
        vm.OpenNote("d.md", "d", true);

        await vm.CloseToRightAsync(b);

        Assert.Equal(new[] { "note:a.md", "note:b.md" }, vm.Tabs.Select(t => t.Key));
        Assert.Contains(a, vm.Tabs);
    }

    [Fact]
    public async Task CloseAllGuardedAsync_closes_everything()
    {
        var vm = new OpenTabsViewModel();
        vm.OpenNote("a.md", "a", true);
        vm.OpenNote("b.md", "b", true);

        await vm.CloseAllGuardedAsync();

        Assert.Empty(vm.Tabs);
        Assert.Null(vm.Active);
    }

    [Fact]
    public async Task Scoped_close_stops_when_a_save_is_vetoed()
    {
        var vm = new OpenTabsViewModel();
        vm.OpenNote("a.md", "a", true);
        var b = vm.OpenNote("b.md", "b", true);
        vm.OpenNote("c.md", "c", true);
        vm.ClosingAsync = _ => Task.FromResult(false);   // user cancels the save prompt

        await vm.CloseOthersAsync(b);

        Assert.Equal(3, vm.Tabs.Count);   // the veto aborted the whole bulk close
    }
}
