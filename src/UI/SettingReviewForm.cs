using static Translator.AppTheme;

namespace Translator;

internal sealed class SettingReviewForm : Form
{
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EditMode = DataGridViewEditMode.EditOnEnter,
        BackgroundColor = Surface, BorderStyle = BorderStyle.None
    };

    private readonly bool destinationObjectsOnly;
    internal IReadOnlyDictionary<string, int> Selections { get; private set; }

    internal SettingReviewForm(IReadOnlyList<SettingDifference> differences, bool destinationObjectsOnly = false)
    {
        this.destinationObjectsOnly = destinationObjectsOnly;
        Selections = differences.ToDictionary(item => item.Key, StartingIndex, StringComparer.Ordinal);
        Text = destinationObjectsOnly
            ? T("Destination object settings", "Impostazioni del pezzo dalla destinazione")
            : T("Review process settings", "Rivedi le impostazioni di stampa");
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ClientSize = new Size(1040, 570);
        MinimumSize = new Size(720, 440);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = Text, Font = new Font("Segoe UI Semibold", 17F), AutoSize = true,
            ForeColor = Ink
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = destinationObjectsOnly
                ? T("The destination is selected for every setting. Review walls, infill, supports and other settings for the part.",
                    "Tutte le voci partono dalla destinazione. Puoi rivedere pareti, riempimento, supporti e altre impostazioni del pezzo.")
                : T("The suggested values use source settings for the part and destination values for calibrations such as speed and flow.",
                    "I valori suggeriti usano le impostazioni di origine per il pezzo e quelle di destinazione per calibrazioni come velocità e flusso."),
            Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(3, 5, 3, 12)
        }, 0, 1);
        var search = new TextBox
        {
            PlaceholderText = T("Filter by setting name…", "Filtra per nome impostazione…"),
            AccessibleName = T("Filter by setting name", "Filtra per nome impostazione"),
            Dock = DockStyle.Top, Margin = new Padding(3, 0, 3, 8)
        };
        search.TextChanged += (_, _) =>
        {
            grid.EndEdit();
            grid.CurrentCell = null;
            string query = search.Text.Trim();
            foreach (DataGridViewRow row in grid.Rows)
                row.Visible = ((SettingDifference)row.Tag!).DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
        };
        layout.Controls.Add(search, 0, 2);

        grid.AccessibleName = T("Setting differences", "Differenze tra le impostazioni");
        if (Dark)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.GridColor = Border;
            grid.DefaultCellStyle.BackColor = Input;
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = AccentLight;
            grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        }
        grid.EditingControlShowing += (_, args) =>
        {
            if (args.Control is ComboBox choices)
                choices.DropDownWidth = Math.Max(choices.Width, 425);
        };
        grid.Columns.Add(ReadOnlyColumn(T("Setting", "Impostazione"), 30));
        grid.Columns.Add(ReadOnlyColumn(T("Source", "Origine"), 19));
        grid.Columns.Add(ReadOnlyColumn(T("Destination", "Destinazione"), 19));
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = T("Value to save", "Valore da salvare"), FillWeight = 32, SortMode = DataGridViewColumnSortMode.NotSortable,
            FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard
        });
        var visible = destinationObjectsOnly ? differences.Where(item => item.IsObjectSetting && item.Options.Count > 1) : differences;
        foreach (SettingDifference difference in visible.OrderBy(item => item.Scope is not null)
                     .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture))
        {
            var row = grid.Rows[grid.Rows.Add(difference.DisplayName, difference.SourceValue, difference.DestinationValue)];
            row.Cells[0].ToolTipText = difference.DisplayName;
            row.Tag = difference;
            var choice = (DataGridViewComboBoxCell)row.Cells[3];
            choice.Items.AddRange(difference.Options.Select(option => option.Label).ToArray());
            choice.Value = difference.Options[StartingIndex(difference)].Label;
            if (difference.Options.Count == 1)
            {
                choice.ReadOnly = true;
                choice.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
                choice.Style.ForeColor = Muted;
            }
        }
        var gridInset = new RoundedPanel(() => Surface) { Dock = DockStyle.Fill, Padding = new Padding(8) };
        gridInset.Controls.Add(grid);
        layout.Controls.Add(gridInset, 0, 3);

        var apply = new RoundedButton(primary: true) { Text = T("Apply and save", "Applica e salva") };
        var cancel = new RoundedButton { Text = T("Cancel", "Annulla"), DialogResult = DialogResult.Cancel };
        var suggested = new RoundedButton
        {
            Text = destinationObjectsOnly
                ? T("Reset to destination", "Ripristina destinazione")
                : T("Reset to suggested values", "Ripristina valori suggeriti")
        };
        apply.Click += (_, _) => Apply();
        suggested.Click += (_, _) => ResetValues();
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        actions.Controls.AddRange([apply, cancel, suggested]);
        if (!destinationObjectsOnly)
        {
            var destination = new RoundedButton { Text = T("Keep all destination values", "Mantieni tutti i valori di destinazione") };
            destination.Click += (_, _) => ResetValues(useDestination: true);
            actions.Controls.Add(destination);
        }
        layout.Controls.Add(actions, 0, 4);
        AcceptButton = apply;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    private static DataGridViewTextBoxColumn ReadOnlyColumn(string title, float weight) => new()
    {
        HeaderText = title, FillWeight = weight, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable
    };

    private int StartingIndex(SettingDifference difference) =>
        destinationObjectsOnly ? SettingDifference.DestinationIndex : difference.ObjectSettingsIndex;

    private void ResetValues(bool useDestination = false)
    {
        grid.EndEdit();
        foreach (DataGridViewRow row in grid.Rows)
        {
            var difference = (SettingDifference)row.Tag!;
            int index = useDestination ? SettingDifference.DestinationIndex : StartingIndex(difference);
            row.Cells[3].Value = difference.Options[index].Label;
        }
    }

    private void Apply()
    {
        if (!grid.EndEdit())
            return;
        var selections = new Dictionary<string, int>(Selections, StringComparer.Ordinal);
        foreach (DataGridViewRow row in grid.Rows)
        {
            var difference = (SettingDifference)row.Tag!;
            for (int index = 0; index < difference.Options.Count; index++)
            {
                if (Equals(row.Cells[3].Value, difference.Options[index].Label))
                {
                    selections[difference.Key] = index;
                    break;
                }
            }
        }
        Selections = selections;
        DialogResult = DialogResult.OK;
    }
}
