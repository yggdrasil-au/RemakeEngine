using System;
using System.Windows.Forms;
using RemakeEngine.Core;

namespace RemakeEngine.Interface.GUI;

public static class WinFormsGui
{
    public static int Run(OperationsEngine engine)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new WinForms.MainForm(engine));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GUI error: {ex.Message}");
            return 1;
        }
    }
}

