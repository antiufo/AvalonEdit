using System;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ICSharpCode.AvalonEdit.CodeCompletion
{
	internal class DwmDropShadow
	{
		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

		[DllImport("dwmapi.dll")]
		private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarInset);

		public static void DropShadowToWindow(Window window)
		{
			if (!DwmDropShadow.DropShadow(window))
			{
				window.SourceInitialized += new EventHandler(DwmDropShadow.window_SourceInitialized);
			}
		}

		private static void window_SourceInitialized(object obj, EventArgs eventArgs)
		{
			Window window = (Window)obj;
			DwmDropShadow.DropShadow(window);
			window.SourceInitialized -= new EventHandler(DwmDropShadow.window_SourceInitialized);
		}

		private static bool DropShadow(Window window)
		{
			bool result;
			try
			{
				WindowInteropHelper windowInteropHelper = new WindowInteropHelper(window);
				int num = 2;
				if (DwmDropShadow.DwmSetWindowAttribute(windowInteropHelper.Handle, 2, ref num, 4) == 0)
				{
					result = true;
				}
				else
				{
					result = false;
				}
			}
			catch (Exception)
			{
				result = false;
			}
			return result;
		}
	}
}
