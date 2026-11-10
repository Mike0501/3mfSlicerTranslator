using static Translator.AppTheme;

namespace Translator;

internal sealed class FilamentMappingForm : Form
{
    private readonly List<ComboBox> mappings = [];

    internal int[] Destinations => mappings.Select(picker => (int)picker.SelectedItem!).ToArray();

    internal FilamentMappingForm(int[] slots, string[] colors, int capacity)
    {
        Text = T("Map filament slots", "Mappa gli slot filamento");
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F);
        BackColor = Canvas;
        ForeColor = Ink;
        ClientSize = new Size(570, 385);
        MinimumSize = new Size(490, 325);
        MinimizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = T($"{colors.Length} source slots · {capacity} destination slots", $"{colors.Length} slot di origine · {capacity} slot di destinazione"),
            AutoSize = true, Font = new Font("Segoe UI Semibold", 15F)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = T("Choose a destination for each used filament. Shared slots use the color of the first assigned source slot.",
                "Scegli la destinazione per ogni filamento usato. Gli slot condivisi prendono il colore del primo slot di origine assegnato."),
            AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Muted, Margin = new Padding(3, 5, 3, 12)
        }, 0, 1);

        var inset = new RoundedPanel(() => Surface) { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2 };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        var sourceColors = slots.Select(slot => ColorTranslator.FromHtml(colors[slot - 1])).ToArray();
        object[] destinations = Enumerable.Range(1, capacity).Cast<object>().ToArray();
        for (int index = 0; index < slots.Length; index++)
        {
            int slot = slots[index];
            string label = T($"Source slot {slot}", $"Slot di origine {slot}");
            var source = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            source.Controls.Add(new Panel { Width = 18, Height = 18, BackColor = sourceColors[index], BorderStyle = BorderStyle.FixedSingle });
            source.Controls.Add(new Label { Text = $"{label} · {colors[slot - 1]}", AutoSize = true });
            var picker = new ComboBox
            {
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Input, ForeColor = Ink,
                AccessibleName = T($"Destination for source slot {slot}", $"Destinazione dello slot di origine {slot}")
            };
            picker.Items.AddRange(destinations);
            int current = index;
            picker.SelectedIndex = index < capacity ? index : Enumerable.Range(0, capacity)
                .MinBy(candidate => Distance(sourceColors[current], sourceColors[candidate]));
            mappings.Add(picker);
            rows.Controls.Add(source, 0, index);
            rows.Controls.Add(picker, 1, index);
        }
        inset.Controls.Add(rows);
        layout.Controls.Add(inset, 0, 2);

        var apply = new RoundedButton(primary: true) { Text = T("Apply mapping", "Applica mappatura"), DialogResult = DialogResult.OK };
        var cancel = new RoundedButton { Text = T("Cancel", "Annulla"), DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([apply, cancel]);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        AcceptButton = apply;
        CancelButton = cancel;
    }

    private static int Distance(Color first, Color second)
    {
        int red = first.R - second.R, green = first.G - second.G, blue = first.B - second.B;
        return red * red + green * green + blue * blue;
    }
}
