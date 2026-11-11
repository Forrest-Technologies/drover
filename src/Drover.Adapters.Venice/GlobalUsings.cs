global using System;
global using System.ClientModel;
global using System.ClientModel.Primitives;
global using System.Collections.Generic;
global using System.Globalization;
global using System.Linq;
global using System.Net;
global using System.Net.Http;
global using System.Net.Http.Headers;
global using System.Runtime.CompilerServices;
global using System.Text.Json;
global using System.Threading;
global using System.Threading.Tasks;
global using Drover.Domain.Models;
global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Http.Resilience;
global using OpenAI;
global using Polly;

// OpenAI.Chat also defines ChatMessage, which Microsoft.Extensions.AI owns here. Only these two are needed from it.
global using ChatCompletion = OpenAI.Chat.ChatCompletion;
global using StreamingChatCompletionUpdate = OpenAI.Chat.StreamingChatCompletionUpdate;
