using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.Tools;

public sealed class ProjectorLayout : IEquatable<ProjectorLayout>
{

    public const int MAX_TILED_PROJECTORS = 4;

    private readonly int[] _usedCells;

    public string Id { get; }

    public int ColumnCount { get; }

    public int RowCount { get; }

    public int CellCount => ColumnCount * RowCount;

    public IReadOnlyList<int> UsedCells => _usedCells;

    private ProjectorLayout(int columns, int rows, int[] usedCells)
    {
        ColumnCount = columns;
        RowCount = rows;
        _usedCells = usedCells;
        Id = $"{columns}x{rows}:{string.Join(',', usedCells)}";
    }

    public static IReadOnlyList<ProjectorLayout> GetLayouts(int windowCount, double aspectRatio)
    {
        windowCount = Math.Min(windowCount, MAX_TILED_PROJECTORS);

        if (windowCount == 1)
        {
            return
            [
                Halves(true, 0),
                Halves(true, 1),
                Halves(false, 0),
                Halves(false, 1),
                Quarter(0),
                Quarter(1),
                Quarter(2),
                Quarter(3),
            ];
        }

        if (windowCount == 2)
        {
            return [Halves(true, 0, 1), Halves(false, 0, 1)];
        }

        if (windowCount > 2) return [GetGrid(windowCount, aspectRatio)];

        return [];
    }

    public static ProjectorLayout GetDefault(int windowCount, double aspectRatio)
    {
        windowCount = Math.Min(windowCount, MAX_TILED_PROJECTORS);

        var isLandscape = aspectRatio >= 1;
        if (windowCount <= 1) return Halves(isLandscape, 0);
        if (windowCount == 2) return Halves(isLandscape, 0, 1);

        return GetGrid(windowCount, aspectRatio);
    }

    public static double GetAspectRatio(PixelRect area)
    {
        if (area.Width <= 0 || area.Height <= 0) return 16d / 9;

        return (double)area.Width / area.Height;
    }

    public PixelRect GetCellBounds(PixelRect area, int cell)
    {
        var column = cell % ColumnCount;
        var row = cell / ColumnCount;

        var left = area.X + area.Width * column / ColumnCount;
        var right = area.X + area.Width * (column + 1) / ColumnCount;
        var top = area.Y + area.Height * row / RowCount;
        var bottom = area.Y + area.Height * (row + 1) / RowCount;

        return new PixelRect(left, top, right - left, bottom - top);
    }

    public Rect GetCellBounds(Rect area, int cell)
    {
        var column = cell % ColumnCount;
        var row = cell / ColumnCount;
        var cellWidth = area.Width / ColumnCount;
        var cellHeight = area.Height / RowCount;

        return new Rect(area.X + column * cellWidth, area.Y + row * cellHeight, cellWidth, cellHeight);
    }

    public int GetProjectorIndex(int cell) => Array.IndexOf(_usedCells, cell);

    public bool Equals(ProjectorLayout? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as ProjectorLayout);
    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Id;

    public static bool operator ==(ProjectorLayout? left, ProjectorLayout? right) => Equals(left, right);
    public static bool operator !=(ProjectorLayout? left, ProjectorLayout? right) => !Equals(left, right);

    private static ProjectorLayout Halves(bool isSideBySide, params int[] usedCells)
    {
        return isSideBySide
            ? new ProjectorLayout(2, 1, usedCells)
            : new ProjectorLayout(1, 2, usedCells);
    }

    private static ProjectorLayout Quarter(int cell) => new(2, 2, [cell]);

    private static ProjectorLayout GetGrid(int windowCount, double aspectRatio)
    {
        var longSide = 2;
        var shortSide = 2;
        while (longSide * shortSide < windowCount)
        {
            if (longSide == shortSide) longSide++;
            else shortSide++;
        }

        var usedCells = Enumerable.Range(0, windowCount).ToArray();
        var isLandscape = aspectRatio >= 1;
        if (isLandscape) return new ProjectorLayout(longSide, shortSide, usedCells);

        return new ProjectorLayout(shortSide, longSide, usedCells);
    }

}

public readonly record struct ProjectorTile(ProjectorLayout Layout, int Cell);
