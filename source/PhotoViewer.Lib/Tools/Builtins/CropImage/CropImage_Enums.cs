namespace PhotoViewer.Tools;

public enum SelectionAspectRatio
{
    FreeRatio = 0,
    Custom = 1,
    Original = 2,
    Ratio1_1 = 3,
    Ratio1_2 = 4,
    Ratio2_1 = 5,
    Ratio2_3 = 6,
    Ratio3_2 = 7,
    Ratio3_4 = 8,
    Ratio4_3 = 9,
    Ratio9_16 = 10,
    Ratio16_9 = 11,
}

public enum DefaultSelectionType
{
    UseTheLastSelection,
    CustomArea,
    SelectAll,
    SelectNone,
    Select10Percent,
    Select20Percent,
    Select25Percent,
    Select30Percent,
    SelectOneThird,
    Select40Percent,
    Select50Percent,
    Select60Percent,
    SelectTwoThirds,
    Select70Percent,
    Select75Percent,
    Select80Percent,
    Select90Percent,
}
