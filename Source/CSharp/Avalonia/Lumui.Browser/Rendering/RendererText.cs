namespace Lumui.Browser.Rendering;

public static class RendererText
{
    public const String Action = "Action";
    public const String Back = "Back";
    public const String Chart = "Chart";
    public const String Column = "Column";
    public const String Component = "component";
    public const String Continue = "Continue";
    public const String DeviceFunction = "Device function";
    public const String Done = "Done";
    public const String EndDate = "End date";
    public const String False = "False";
    public const String FunctionUnavailable = "This function is not available on the current device.";
    public const String Guided = "Guided";
    public const String Home = "Home";
    public const String Image = "Image";
    public const String Item = "Item";
    public const String LinearReadingOrder = "Linear reading order";
    public const String LinearReadingOrderDescription =
        "Linear reading order. Headings and controls follow document order.";
    public const String Links = "Links";
    public const String Lumui = "LUMUI";
    public const String More = "More";
    public const String Next = "Next";
    public const String Page = "Page";
    public const String Pages = "Pages";
    public const String NoPage = "No page is available.";
    public const String Open = "Open";
    public const String Option = "Option";
    public const String Progress = "Progress";
    public const String PreviewUnavailable = "No component is available for this preview.";
    public const String Sections = "Sections";
    public const String StartDate = "Start date";
    public const String Status = "Status";
    public const String Preview = "Preview";
    public const String True = "True";
    public const String Unsupported = "Unsupported ";
    public const String Value = "Value";

    public static String Step(Int32 current, Int32 total) =>
        $"Step {current} of {total}";

    public static String Position(Int32 current, Int32 total) =>
        $"{current} of {total}";

    public static String Section(Int32 index) =>
        $"Section {index}";

    public static String Presentation(String title, String profile) =>
        title + ", " + profile + " presentation";

    public static String ImageUnavailable(String detail) =>
        "Image unavailable: " + detail;
}
