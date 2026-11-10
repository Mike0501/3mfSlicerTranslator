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

    internal IReadOnlyDictionary<string, int> Selections { get; private set; } = new Dictionary<string, int>();

    internal SettingReviewForm(IReadOnlyList<SettingDifference> differences)
    {
        Text = T("Review process settings", "Rivedi le impostazioni di stampa");
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
            Text = T("Choose the value to save. Settings without a compatible source value follow the destination profile.",
                "Scegli il valore da salvare. Le voci senza un valore di origine compatibile seguono il profilo di destinazione."),
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
                row.Visible = ((SettingDifference)row.Tag!).Key.Contains(query, StringComparison.OrdinalIgnoreCase);
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
        foreach (SettingDifference difference in differences.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var row = grid.Rows[grid.Rows.Add(difference.Key, difference.SourceValue, difference.DestinationValue)];
            row.Tag = difference;
            var choice = (DataGridViewComboBoxCell)row.Cells[3];
            choice.Items.AddRange(difference.Options.Select(option => option.Label).ToArray());
            choice.Value = difference.Options[difference.DefaultIndex].Label;
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
        var suggested = new RoundedButton { Text = T("Use suggested values", "Usa valori suggeriti") };
        var destination = new RoundedButton { Text = T("Keep all destination values", "Mantieni tutti i valori di destinazione") };
        apply.Click += (_, _) => Apply();
        suggested.Click += (_, _) => SelectAll(useSuggested: true);
        destination.Click += (_, _) => SelectAll(useSuggested: false);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        actions.Controls.AddRange([apply, cancel, suggested, destination]);
        layout.Controls.Add(actions, 0, 4);
        AcceptButton = apply;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    private static DataGridViewTextBoxColumn ReadOnlyColumn(string title, float weight) => new()
    {
        HeaderText = title, FillWeight = weight, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable
    };

    private void SelectAll(bool useSuggested)
    {
        grid.EndEdit();
        foreach (DataGridViewRow row in grid.Rows)
        {
            var difference = (SettingDifference)row.Tag!;
            row.Cells[3].Value = difference.Options[useSuggested ? difference.DefaultIndex : SettingDifference.DestinationIndex].Label;
        }
    }

    private void Apply()
    {
        if (!grid.EndEdit())
            return;
        var selections = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DataGridViewRow row in grid.Rows)
        {
            var difference = (SettingDifference)row.Tag!;
            for (int index = 0; index < difference.Options.Count; index++)
            {
                if (Equals(row.Cells[3].Value, difference.Options[index].Label))
                {
                    selections.Add(difference.Key, index);
                    break;
                }
            }
        }
        Selections = selections;
        DialogResult = DialogResult.OK;
    }
}
