using System.Collections.Generic;
using ServerAssistMod;

/// <summary>
/// Local console command (F1) for clients. The command is intentionally
/// client-only: it opens the window locally and lets the net package ask the
/// server for data.
/// </summary>
public class ConsoleCmdServerAssist : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient
    {
        get { return true; }
    }

    public override int DefaultPermissionLevel
    {
        get { return 0; }
    }

    public override string[] getCommands()
    {
        return new[] { "serverassist", "sa" };
    }

    public override string getDescription()
    {
        return "Open the Server Assist button board.";
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        ServerAssistClient.ToggleHome();
    }
}




