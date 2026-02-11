var username = Environment.UserName;
Console.WriteLine($"Hello {username}! This is the Kong programming language!");
Console.WriteLine("Feel free to type in commands");
Kong.Repl.Repl.Start(Console.In, Console.Out);
