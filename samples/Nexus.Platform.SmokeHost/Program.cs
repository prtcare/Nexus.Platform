using Nexus.Platform.SmokeHost;

// M-01-6.1 live smoke host.
//
//   SmokeHost send "<prompt>" [modelId]   -> runs a real OpenAI turn through the routing
//                                            gateway, persists it, prints the record
//   SmokeHost recv <id>                    -> prints the persisted assistant message; run in a
//                                            FRESH process to prove it survived a restart

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: SmokeHost send \"<prompt>\" [modelId] | SmokeHost recv <id>");
    return 1;
}

switch (args[0].ToLowerInvariant())
{
    case "send":
    {
        var prompt = args.Length > 1 ? args[1] : "Reply with exactly one word: pong";
        var model = args.Length > 2 ? args[2] : null;

        var turn = await SmokeRunner.SendTurnAsync(prompt, model);
        var r = turn.Record;

        Console.WriteLine($"ID={r.Id}");
        Console.WriteLine($"MODEL={r.ModelUsed}");
        Console.WriteLine($"TOKENS_IN={r.TokensIn}");
        Console.WriteLine($"TOKENS_OUT={r.TokensOut}");
        Console.WriteLine($"ASSISTANT={r.AssistantContent}");
        Console.WriteLine($"RECORD={Path.Combine(ChatStore.DataDirectory, r.Id + ".json")}");

        return string.IsNullOrWhiteSpace(r.AssistantContent) ? 2 : 0;
    }

    case "recv":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("recv requires <id>");
            return 3;
        }

        var content = SmokeRunner.ReadAssistantMessage(args[1]);
        Console.WriteLine(content ?? $"<no record {args[1]}>");
        return content is null ? 4 : 0;
    }

    default:
        Console.Error.WriteLine($"unknown command: {args[0]}");
        return 5;
}
