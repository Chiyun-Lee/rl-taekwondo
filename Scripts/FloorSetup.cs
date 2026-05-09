using Godot;

[Tool]
public partial class FloorSetup : StaticBody3D
{
	const string ShaderPath = "res://Assets/Materials/CheckerFloor.gdshader";

	public override void _Ready()
	{
		var mesh = GetNodeOrNull<MeshInstance3D>("FloorMesh");
		if (mesh == null) return;

		var shader   = GD.Load<Shader>(ShaderPath);
		var material = new ShaderMaterial { Shader = shader };
		mesh.MaterialOverride = material;
	}
}
