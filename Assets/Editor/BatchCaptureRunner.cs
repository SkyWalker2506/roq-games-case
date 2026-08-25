#if UNITY_EDITOR
using UnityEditor;

public static class BatchCaptureRunner
{
    public static void CaptureDenseCase2()
    {
        FrameStripCapture.SetFrameCount(254);
        FrameStripCapture.Capture("BlockHole");
    }

    public static void CaptureDenseCase3()
    {
        FrameStripCapture.SetFrameCount(240);
        FrameStripCapture.Capture("Stickerdom");
    }

    public static void CaptureDenseCase4()
    {
        FrameStripCapture.SetFrameCount(340);
        FrameStripCapture.Capture("Buca");
    }

    public static void CaptureOnlyCase1Video()
    {
        FrameStripCapture.SetFrameCount(180);
        FrameStripCapture.Capture("FitTheShape");
    }

    public static void CaptureOnlyCase2Video()
    {
        FrameStripCapture.SetFrameCount(254);
        FrameStripCapture.Capture("BlockHole");
    }

    public static void CaptureOnlyCase3Video()
    {
        FrameStripCapture.SetFrameCount(240);
        FrameStripCapture.Capture("Stickerdom");
    }

    public static void CaptureOnlyCase4Video()
    {
        FrameStripCapture.SetFrameCount(340);
        FrameStripCapture.Capture("Buca");
    }
}
#endif
