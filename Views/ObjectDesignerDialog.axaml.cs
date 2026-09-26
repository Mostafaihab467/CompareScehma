using System.Collections.ObjectModel;
using Avalonia.Controls;
using SchemaCompare.Controls;
using SchemaCompare.Models;
using SchemaCompare.Services;

namespace SchemaCompare.Views;

public partial class ObjectDesignerDialog : Window
{
    private readonly ObservableCollection<DesignerColumn> _columns = [];
    private bool _finished;

    private ObjectDesignerDialog(DesignerKind kind, string schema)
    {
        InitializeComponent();

        KindBox.SelectedIndex = (int)kind;
        SchemaBox.Text = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema;
        ColumnsGrid.ItemsSource = _columns;

        for (var i = 0; i < 3; i++)
            _columns.Add(new DesignerColumn { Name = i == 0 ? "Id" : "", Type = i == 0 ? "int" : "nvarchar(200)" });
        _columns[0].IsKey = true;
        _columns[0].IsIdentity = true;
        _columns[1].Name = "Name";
        _columns[1].Nullable = false;

        KindBox.SelectionChanged += (_, _) => { ApplyKind(); UpdateScript(); };
        SchemaBox.TextChanged += (_, _) => UpdateScript();
        NameBox.TextChanged += (_, _) => UpdateScript();
        ColumnsGrid.CellEditEnding += (_, _) => UpdateScript();
        AddColBtn.Click += (_, _) =>
        {
            _columns.Add(new DesignerColumn());
            ColumnsGrid.SelectedItem = _columns[^1];
            UpdateScript();
        };
        RemoveColBtn.Click += (_, _) =>
        {
            if (ColumnsGrid.SelectedItem is DesignerColumn col)
            {
                _columns.Remove(col);
                UpdateScript();
            }
        };
        BodyEditor.PropertyChanged += (_, e) =>
        {
            if (e.Property == SqlHighlightedEditor.TextProperty) UpdateScript();
        };

        ScriptBtn.Click += (_, _) => Finish(executeNow: false);
        ExecuteBtn.Click += (_, _) => Finish(executeNow: true);
        CancelBtn.Click += (_, _) => Close(null);

        ApplyKind();
        UpdateScript();
    }

    public static Task<DesignerSpec?> ShowAsync(Window owner, DesignerKind kind, string schema = "dbo")
    {
        var dlg = new ObjectDesignerDialog(kind, schema);
        return dlg.ShowDialog<DesignerSpec?>(owner);
    }

    private DesignerKind SelectedKind => KindBox.SelectedIndex switch
    {
        1 => DesignerKind.View,
        2 => DesignerKind.StoredProcedure,
        _ => DesignerKind.Table
    };

    /// <summary>The table grid and the T-SQL body are alternative editors for one kind.</summary>
    private void ApplyKind()
    {
        var kind = SelectedKind;
        var isTable = kind == DesignerKind.Table;
        ColumnsPanel.IsVisible = isTable;
        BodyPanel.IsVisible = !isTable;
        IconText.Text = kind switch { DesignerKind.View => "👁", DesignerKind.StoredProcedure => "📜", _ => "🗂" };
        HeaderText.Text = $"New {TitleFor(kind)}";
        Title = $"New {TitleFor(kind)}";
        BodyHint.Text = kind == DesignerKind.View
            ? "View body (the SELECT after AS)"
            : "Procedure body (AS … BEGIN … END)";
        // An untouched template follows the kind; anything the operator typed stays.
        var body = BodyEditor.Text ?? "";
        if (body.Length == 0 || body == ViewTemplate || body == BodyTemplate)
            BodyEditor.Text = kind == DesignerKind.View ? ViewTemplate : BodyTemplate;
        NoteText.IsVisible = false;
    }

    private static string TitleFor(DesignerKind kind) => kind switch
    {
        DesignerKind.View => "View",
        DesignerKind.StoredProcedure => "Stored Procedure",
        _ => "Table"
    };

    private const string ViewTemplate = "SELECT 1 AS One;";

    private const string BodyTemplate = """
        @param int = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SELECT @param AS Parameter;
        END
        """;

    private DesignerSpec BuildSpec(bool executeNow) => new()
    {
        Kind = SelectedKind,
        Schema = SchemaBox.Text?.Trim() ?? "dbo",
        Name = NameBox.Text?.Trim() ?? "",
        Columns = SelectedKind == DesignerKind.Table
            ? _columns.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList()
            : [],
        Body = BodyEditor.Text ?? "",
        ExecuteNow = executeNow
    };

    private void UpdateScript()
    {
        try
        {
            Preview.Text = ManagerScriptBuilder.CreateObject(BuildSpec(false));
        }
        catch (Exception ex)
        {
            Preview.Text = $"-- {ex.Message}";
            NoteText.Text = ex.Message;
            NoteText.IsVisible = true;
        }
    }

    private void Finish(bool executeNow)
    {
        if (_finished) return;
        try
        {
            var spec = BuildSpec(executeNow);
            ManagerScriptBuilder.CreateObject(spec);       // refuses what the preview rejected
            _finished = true;
            Close(spec);
        }
        catch (Exception ex)
        {
            NoteText.Text = ex.Message;
            NoteText.IsVisible = true;
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Focus();
            else ColumnsGrid.Focus();
        }
    }
}
