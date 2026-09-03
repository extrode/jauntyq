namespace Extrode.JauntyQ.Generator;

public class ProjectionModel
{
    public string Name { get; set; } = string.Empty;
    public List<ProjectionColumn> Columns { get; set; } = new();
}
