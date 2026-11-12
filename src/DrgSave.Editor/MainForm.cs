using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using DevExpress.XtraEditors;
using DevExpress.XtraTab;
using DrgSave.Core;
using Microsoft.Win32;

namespace DrgSave.Editor;

internal sealed class MainForm : XtraForm
{
    private const string AppTitle = "Deep Rock Galactic Save Editor";

    private readonly XtraTabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly LabelControl _status = new() { Location = new Point(Ui.Pad, 10) };
    private readonly MemoEdit _log = new() { Dock = DockStyle.Fill, Properties = { ReadOnly = true } };
    private DrgSaveFile? _save;
    private bool _dirty;

    public MainForm(string? path)
    {
        Text = $"{AppTitle} - {Ui.Credit}";
        Size = new Size(1400, 900);
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;

        var header = Ui.Panel(DockStyle.Top, 116);
        header.Controls.Add(Ui.Label(AppTitle, Ui.Pad, 16, Ui.TitleFont));
        header.Controls.Add(Ui.CreditLabel(Ui.Pad + 2, 54));
        int x = 440;
        header.Controls.Add(Ui.Button("Open a file...", x, 150, (_, _) => OpenDialog(), 40)); x += 160;
        header.Controls.Add(Ui.Button("Open Steam save", x, 150, (_, _) => OpenSteamSave(), 40)); x += 166;
        header.Controls.Add(Ui.Button("Save", x, 110, (_, _) => SaveFile(null), 40)); x += 118;
        header.Controls.Add(Ui.Button("Save a copy...", x, 130, (_, _) => SaveCopy(), 40)); x += 138;
        header.Controls.Add(Ui.Button("Undo all changes", x, 150, (_, _) => Reload(), 40));

        var footer = Ui.Panel(DockStyle.Bottom, 34);
        footer.Controls.Add(_status);
        var credit = Ui.Label($"{AppTitle}  -  {Ui.Credit}", 0, 10, color: Ui.Accent);
        footer.Controls.Add(credit);
        footer.Resize += (_, _) => credit.Left = footer.Width - credit.Width - Ui.Pad;

        Controls.Add(_tabs);
        Controls.Add(footer);
        Controls.Add(header);

        ShowStart();
        if (path != null) Shown += (_, _) => Open(path);
        FormClosing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
    }

    // ---- files ----------------------------------------------------------------------

    private static string? SteamSaveDir()
    {
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is not string steam) return null;
            var dir = Path.Combine(steam, "steamapps", "common", "Deep Rock Galactic", "FSD", "Saved", "SaveGames");
            return Directory.Exists(dir) ? dir : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    private void OpenSteamSave()
    {
        var dir = SteamSaveDir();
        var file = dir == null ? null : Directory.GetFiles(dir, "*_Player.sav").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
        if (file == null)
        {
            XtraMessageBox.Show(this, "No Steam save was found. Use Open a file... and pick your save.", AppTitle);
            return;
        }
        Open(file);
    }

    private void OpenDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "Save files (*.sav)|*.sav|All files (*.*)|*.*", Title = "Open a decrypted Deep Rock Galactic save" };
        if (dlg.ShowDialog(this) == DialogResult.OK) Open(dlg.FileName);
    }

    private void Open(string path)
    {
        if (!ConfirmDiscard()) return;
        try
        {
            var save = DrgSaveFile.Load(path);
            _save = save;
            _dirty = false;
            Log($"Opened {path} ({new FileInfo(path).Length:N0} bytes).");
            if (!save.RoundTrips())
                Log("WARNING: this save does not write back byte-identical yet. Saving is blocked until the reader is fixed.");
            BuildPages();
            SetStatus($"Opened {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or GvasFormatException)
        {
            XtraMessageBox.Show(this, $"This file could not be opened as a save.\n\n{ex.Message}\n\nIf it is a PS4 save, decrypt it first.", AppTitle);
        }
    }

    private void Reload()
    {
        if (_save == null) return;
        _dirty = false;
        Open(_save.Path);
    }

    private void SaveCopy()
    {
        if (_save == null) return;
        using var dlg = new SaveFileDialog { Filter = "Save files (*.sav)|*.sav", FileName = Path.GetFileName(_save.Path) };
        if (dlg.ShowDialog(this) == DialogResult.OK) SaveFile(dlg.FileName);
    }

    private void SaveFile(string? path)
    {
        if (_save == null) return;
        if (Process.GetProcessesByName("FSD-Win64-Shipping").Length > 0 &&
            XtraMessageBox.Show(this, "The game is running and may overwrite this file.\nClose the game first. Save anyway?", AppTitle, MessageBoxButtons.YesNo) != DialogResult.Yes)
            return;
        try
        {
            string backup = _save.Save(path);
            _dirty = false;
            Log(backup.Length > 0 ? $"Saved {_save.Path}. Old file kept as {backup}." : $"Saved {_save.Path}.");
            SetStatus($"Saved {_save.Path}");
            XtraMessageBox.Show(this, "Saved." + (backup.Length > 0 ? $"\n\nYour old save was kept as:\n{backup}" : ""), AppTitle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or GvasFormatException)
        {
            XtraMessageBox.Show(this, $"Saving failed. Nothing was changed.\n\n{ex.Message}", AppTitle);
        }
    }

    private bool ConfirmDiscard() =>
        !_dirty || XtraMessageBox.Show(this, "You have changes that are not saved. Throw them away?", AppTitle, MessageBoxButtons.YesNo) == DialogResult.Yes;

    private void MarkDirty() { _dirty = true; SetStatus("Changed (not saved yet)"); }

    private void SetStatus(string text) => _status.Text = text;

    private void Log(string text) => _log.Text += $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}";

    // ---- pages ----------------------------------------------------------------------

    private void ShowStart()
    {
        _tabs.TabPages.Clear();
        AddPage("Welcome", BuildStartPage());
        AddPage("About", BuildAboutPage());
    }

    private void BuildPages()
    {
        _tabs.TabPages.Clear();
        AddPage("Resources", BuildResourcesPage());
        AddPage("Dwarves", BuildDwarvesPage());
        AddPage("Seasons", BuildSeasonsPage());
        AddPage("Unlocks", BuildUnlocksPage());
        var tree = new TreePage(MarkDirty, Log);
        tree.Load(_save!.File);
        var advanced = new XtraTabControl { Dock = DockStyle.Fill };
        var t1 = advanced.TabPages.Add("Every field");
        t1.Controls.Add(tree.Control);
        var t2 = advanced.TabPages.Add("Log");
        t2.Controls.Add(_log);
        AddPage("Advanced", advanced);
        AddPage("About", BuildAboutPage());
    }

    private void AddPage(string title, Control content)
    {
        var page = _tabs.TabPages.Add(title);
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
    }

    private Control BuildStartPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        int x = Ui.Pad * 2, y = 36;
        page.Controls.Add(Ui.Label("Welcome", x, y, Ui.TitleFont)); y += 56;
        foreach (var step in new[]
                 {
                     "1.   Export your PS4 save and decrypt it with your save tool.",
                     "2.   Click  Open a file...  and pick the save.",
                     "3.   Change what you want on the pages that appear, then click  Save.",
                     "4.   Re-encrypt the save with your save tool and import it back to the PS4.",
                 })
        {
            page.Controls.Add(Ui.Label(step, x, y, Ui.BigFont));
            y += 34;
        }
        y += 16;
        page.Controls.Add(Ui.Button("Open a file...", x, 180, (_, _) => OpenDialog(), y));
        page.Controls.Add(Ui.Button("Open Steam save", x + 192, 180, (_, _) => OpenSteamSave(), y));
        y += 70;
        page.Controls.Add(Ui.TextBlock(
            "A backup of your old save is kept next to it every time you click Save.\n\n" +
            "Tested in game on PS4. Paid DLC items are never unlocked.",
            x, y, 900, 80));
        y += 92;
        page.Controls.Add(Ui.TextBlock(
            "Warning: Deep Rock Galactic has online co-op. Edited saves are used at your own risk.",
            x, y, 900, 44, color: Ui.Warning));
        y += 56;
        page.Controls.Add(Ui.TextBlock(
            $"This editor is free. If someone sold it to you, or sold you an edited save, you were ripped off.  {Ui.Credit}.",
            x, y, 900, 44, Ui.BigFont, Ui.Accent));
        return page;
    }

    /// <summary>Credits, perk points and every resource, each with a number box.</summary>
    private Control BuildResourcesPage()
    {
        var page = new XtraScrollableControl { Dock = DockStyle.Fill };
        int x = Ui.Pad * 2, y = 24;
        page.Controls.Add(Ui.Label("Money and points", x, y, Ui.HeadingFont)); y += 40;
        y = NumberRow(page, "Credits", x, y, _save!.GetInt(DrgKnown.Credits, out _),
            v => _save.SetInt(DrgKnown.Credits, v), max: 99_999_999);
        y = NumberRow(page, "Perk points", x, y, _save.GetInt(DrgKnown.PerkPoints, out _) ?? 0,
            v => _save.EnsureRootInt(DrgKnown.PerkPoints).Value = v, max: 9_999);

        bool hasList = _save.GuidMap(DrgKnown.OwnedResources) != null;
        if (!hasList)
            page.Controls.Add(Ui.TextBlock("This save has no resources yet. Changing one below adds the list the game uses.", x, y + 6, 900, 30, color: Ui.Dim));
        y += hasList ? 0 : 34;

        foreach (var group in DrgKnown.Resources.GroupBy(r => r.Group))
        {
            y += 16;
            page.Controls.Add(Ui.Label(group.Key, x, y, Ui.HeadingFont)); y += 40;
            foreach (var r in group)
            {
                var res = r;
                y = NumberRow(page, r.Name, x, y, (int)_save.GetResource(r.Guid),
                    v => _save.SetResource(res.Guid, v), max: 99_999);
            }
        }
        return page;
    }

    /// <summary>A labelled number box with a Max button. <paramref name="set"/> runs on every change.</summary>
    private int NumberRow(Control page, string label, int x, int y, int? value, Action<int> set, int max)
    {
        page.Controls.Add(Ui.Label(label, x, y + 4, Ui.BigFont));
        if (value == null)
        {
            page.Controls.Add(Ui.Label("not in this save", x + 260, y + 6, color: Ui.Dim));
            return y + 40;
        }
        var spin = Ui.WholeNumber(0, max);
        spin.Bounds = new Rectangle(x + 260, y, 200, 30);
        spin.Value = Math.Min(value.Value, max);
        spin.EditValueChanged += (_, _) => { set((int)spin.Value); MarkDirty(); };
        page.Controls.Add(spin);
        page.Controls.Add(Ui.Button("Max", x + 470, 70, (_, _) => spin.Value = max, y - 3));
        return y + 40;
    }

    /// <summary>XP and promotions for each class.</summary>
    private Control BuildDwarvesPage()
    {
        var page = new XtraScrollableControl { Dock = DockStyle.Fill };
        int x = Ui.Pad * 2, y = 24;
        var chars = _save!.Characters();
        foreach (var d in DrgKnown.Dwarves)
        {
            page.Controls.Add(Ui.Label(d.Name, x, y, Ui.HeadingFont)); y += 40;
            var rec = chars.FirstOrDefault(c => string.Equals(c.id, d.Guid, StringComparison.OrdinalIgnoreCase)).record;
            if (rec == null)
            {
                page.Controls.Add(Ui.Label("not found in this save", x, y, color: Ui.Dim));
                y += 40;
                continue;
            }
            y = FieldRow(page, "Class XP", rec, "XP", x, y, 10_000_000);
            y = FieldRow(page, "Promotions", rec, "TimesRetired", x, y, 9_999);
            y += 12;
        }
        return page;
    }

    /// <summary>A number box bound to an int field of a struct.</summary>
    private int FieldRow(Control page, string label, GvasStruct s, string field, int x, int y, int max)
    {
        var p = DrgSaveFile.Field(s, field);
        return NumberRow(page, label, x, y, p?.Value as int?, v => p!.Value = v, max);
    }

    /// <summary>
    /// Schematics (overclocks and Forge cosmetics) and per-class cosmetics. Paid DLC items are
    /// never added.
    /// </summary>
    private Control BuildUnlocksPage()
    {
        var page = new XtraScrollableControl { Dock = DockStyle.Fill };
        int x = Ui.Pad * 2, y = 24;
        page.Controls.Add(Ui.TextBlock(
            "EXPERIMENTAL: test on a copy of your save first. Paid DLC items are never added.",
            x, y, 900, 26, color: Ui.Warning));
        y += 36;

        page.Controls.Add(Ui.Label("Forge schematics", x, y, Ui.HeadingFont)); y += 34;
        page.Controls.Add(Ui.TextBlock(
            "Adds schematics as found but not forged, as if they dropped in a mission. Forge them at the Forge in the Space Rig (max your resources on the Resources page first).",
            x, y, 900, 44, color: Ui.Dim));
        y += 52;
        var owned = DrgSaveFile.Ids(_save!.SchematicList("OwnedSchematics", false));
        var forged = DrgSaveFile.Ids(_save.SchematicList("ForgedSchematics", false));
        foreach (var (category, label) in DrgItems.SchematicGroups)
        {
            var ids = DrgItems.All.Where(i => i.Kind == "Schematic" && i.Category == category && !i.Dlc).Select(i => i.Id).ToList();
            if (ids.Count == 0) continue;
            int have = ids.Count(id => owned.Contains(id) || forged.Contains(id));
            var status = Ui.Label($"{have} of {ids.Count} found or forged", x + 300, y + 9, color: Ui.Dim);
            page.Controls.Add(Ui.Label(label, x, y + 7, Ui.BigFont));
            page.Controls.Add(status);
            page.Controls.Add(Ui.Button("Give all", x + 560, 120, (_, _) =>
            {
                int added = _save.AddOwnedSchematics(ids);
                if (added > 0) MarkDirty();
                Log($"{label}: {added} schematics added.");
                status.Text = $"{ids.Count} of {ids.Count} found or forged";
            }, y));
            y += 44;
        }

        y += 16;
        page.Controls.Add(Ui.Label("Cosmetics for every class", x, y, Ui.HeadingFont)); y += 34;
        var vanity = DrgItems.All.Where(i => i.Kind.EndsWith("VanityItem", StringComparison.Ordinal) && !i.Dlc).Select(i => i.Id).ToList();
        var poses = DrgItems.All.Where(i => i.Kind == "VictoryPose" && !i.Dlc).Select(i => i.Id).ToList();
        y = UnlockRow(page, "Armour, headwear, beards, paints", "Vanity.UnLockedVanityItemIDs", vanity, x, y,
            alsoAdd: () => DrgSaveFile.AddIds(_save.RootGuidArray("UnLockedVanityItemIDs"), vanity));
        y = UnlockRow(page, "Victory poses", "VictoryPose.UnlockedVictoryPoses", poses, x, y);

        y += 16;
        page.Controls.Add(Ui.Label("Weapons and gear", x, y, Ui.HeadingFont)); y += 40;
        var items = DrgItems.All;
        // Only class equipment: event pods and the like have no owning class.
        var weapons = items.Where(i => i.Kind == "ItemID" && !i.Dlc && i.Owner is "Driller" or "Engineer" or "Gunner" or "Scout").Select(i => i.Id).ToList();
        var upgrades = items.Where(i => i.IsWeaponUpgrade && !i.Dlc).Select(i => i.Id).ToList();
        var skins = items.Where(i => i.Kind == "ItemSkin" && !i.Dlc && i.Item.Length > 0).Select(i => (i.Item, i.Id)).ToList();
        var pickaxe = items.Where(i => i.Kind == "PickaxePart" && !i.Dlc).Select(i => i.Id).ToList();
        var drinks = items.Where(i => i.Kind == "DrinkableDataAsset" && !i.Dlc).Select(i => i.Id).ToList();
        y = RootRow(page, "Weapons and tools", weapons.Count, () =>
        {
            // The game keeps both lists: unlocked (can buy) and owned (bought).
            int added = DrgSaveFile.AddIds(_save.RootGuidArray("UnlockedItems"), weapons);
            DrgSaveFile.AddIds(_save.RootGuidArray("OwnedItems"), weapons);
            return added;
        }, x, y);
        y = RootRow(page, "Weapon upgrades (all tiers)", upgrades.Count, () => DrgSaveFile.AddIds(_save.RootGuidArray("PurchasedItemUpgrades"), upgrades), x, y);
        y = RootRow(page, "Weapon paint jobs and frameworks", skins.Count, () => _save.AddItemSkins(skins), x, y);
        y = RootRow(page, "Pickaxe parts", pickaxe.Count, () => DrgSaveFile.AddIds(_save.RootGuidSet("UnlockedPickaxeParts"), pickaxe), x, y);
        y = RootRow(page, "Abyss Bar drinks", drinks.Count, () => DrgSaveFile.AddIds(_save.DrinkSet(), drinks), x, y);
        y += 8;
        page.Controls.Add(Ui.TextBlock("Perks: give yourself perk points on the Resources page and claim the perks in game.", x, y, 900, 30, color: Ui.Dim));
        return page;
    }

    private int RootRow(Control page, string label, int total, Func<int> unlock, int x, int y)
    {
        var status = Ui.Label($"{total} available", x + 300, y + 9, color: Ui.Dim);
        page.Controls.Add(Ui.Label(label, x, y + 7, Ui.BigFont));
        page.Controls.Add(status);
        page.Controls.Add(Ui.Button("Unlock all", x + 560, 120, (_, _) =>
        {
            int added = unlock();
            if (added > 0) MarkDirty();
            Log($"{label}: {added} added.");
            status.Text = added > 0 ? $"{added} added" : "all already unlocked";
        }, y));
        return y + 44;
    }

    private int UnlockRow(Control page, string label, string path, List<string> ids, int x, int y, Func<int>? alsoAdd = null)
    {
        var dwarves = _save!.Characters().Where(c => DrgKnown.Dwarves.Any(d => string.Equals(d.Guid, c.id, StringComparison.OrdinalIgnoreCase))).ToList();
        int Have() => dwarves.Sum(d => DrgSaveFile.Ids(DrgSaveFile.ClassArray(d.record, path)).Count(ids.Contains));
        var status = Ui.Label($"{Have()} of {ids.Count * dwarves.Count} unlocked", x + 300, y + 9, color: Ui.Dim);
        page.Controls.Add(Ui.Label(label, x, y + 7, Ui.BigFont));
        page.Controls.Add(status);
        page.Controls.Add(Ui.Button("Unlock all", x + 560, 120, (_, _) =>
        {
            int added = 0;
            foreach (var d in dwarves)
                if (DrgSaveFile.ClassArray(d.record, path) is { } array) added += DrgSaveFile.AddIds(array, ids);
            if (alsoAdd != null) added += alsoAdd();
            if (added > 0) MarkDirty();
            Log($"{label}: {added} added across {dwarves.Count} classes.");
            status.Text = $"{Have()} of {ids.Count * dwarves.Count} unlocked";
        }, y));
        return y + 44;
    }

    /// <summary>Season XP and scrip for every season in the save.</summary>
    private Control BuildSeasonsPage()
    {
        var page = new XtraScrollableControl { Dock = DockStyle.Fill };
        int x = Ui.Pad * 2, y = 24;
        page.Controls.Add(Ui.TextBlock($"Season XP is the total. Every {DrgKnown.XpPerSeasonLevel:N0} XP is one season level (level 100 = {100 * DrgKnown.XpPerSeasonLevel:N0}).", x, y, 900, 30, color: Ui.Dim));
        y += 44;
        var seasons = _save!.SeasonRecords();
        if (seasons.Count == 0) page.Controls.Add(Ui.Label("This save has no seasons.", x, y, color: Ui.Dim));
        foreach (var (id, rec) in seasons)
        {
            page.Controls.Add(Ui.Label(DrgKnown.ResourceName(id) ?? $"Season {id}", x, y, Ui.HeadingFont)); y += 40;
            y = FieldRow(page, "Season XP", rec, "XP", x, y, 100 * DrgKnown.XpPerSeasonLevel);
            y = FieldRow(page, "Scrip", rec, "Tokens", x, y, 9_999);
            y += 12;
        }
        return page;
    }

    private Control BuildAboutPage()
    {
        var page = Ui.Panel(DockStyle.Fill);
        int x = Ui.Pad * 2, y = 32;
        page.Controls.Add(Ui.Label(AppTitle, x, y, Ui.TitleFont)); y += 44;
        page.Controls.Add(Ui.Label(Ui.Credit, x, y, Ui.TitleFont, Ui.Accent)); y += 56;
        page.Controls.Add(Ui.TextBlock(
            "A free save editor for Deep Rock Galactic saves (decrypted PS4 saves and PC saves).\n" +
            "It is free and always will be. If you paid for this editor or for an edited save, you were ripped off.",
            x, y, 900, 50, Ui.BigFont)); y += 70;

        page.Controls.Add(Ui.Label("Credits", x, y, Ui.HeadingFont)); y += 36;
        foreach (var (who, what, link) in new[]
                 {
                     (Ui.Author, "created the editor", ""),
                     ("DRG-Save-Editor", "public research on the save's fields", "https://github.com/AnthonyMichaelTDM/DRG-Save-Editor"),
                     ("DevExpress WinForms", "the buttons, lists and tables", "https://www.devexpress.com/products/net/controls/winforms/"),
                     (".NET 8", "what the editor runs on", "https://dotnet.microsoft.com/"),
                     ("Deep Rock Galactic", "the game, by Ghost Ship Games", "https://www.deeprockgalactic.com/"),
                 })
        {
            page.Controls.Add(Ui.Label(who, x, y, new Font(Ui.BigFont, FontStyle.Bold), who == Ui.Author ? Ui.Accent : null));
            page.Controls.Add(Ui.Label(what, x + 330, y + 2));
            if (link.Length > 0)
            {
                var url = new HyperlinkLabelControl { Text = link, Location = new Point(x + 720, y + 2) };
                url.HyperlinkClick += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { Log($"Could not open {link}: {ex.Message}"); }
                };
                page.Controls.Add(url);
            }
            y += 34;
        }
        y += 22;
        page.Controls.Add(Ui.TextBlock(
            "This is a fan-made tool. It is not made, approved or supported by Ghost Ship Games or Coffee Stain.",
            x, y, 900, 44));
        return page;
    }
}
