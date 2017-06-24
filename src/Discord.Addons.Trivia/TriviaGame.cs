﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace Discord.Addons.TriviaGames
{
    /// <summary> Creates a Trivia game in a given channel. </summary>
    public sealed class TriviaGame
    {
        private readonly Stack<QA> _triviaData;
        private readonly IMessageChannel _channel;
        private readonly int _turns;
        private readonly Timer _questionTimer;

        private readonly ConcurrentDictionary<ulong, int> _scoreboard = new ConcurrentDictionary<ulong, int>();
        private readonly Random _rng = new Random();
        private readonly Atomic<bool> _isAnswered = new Atomic<bool>(true);

        private QA _currentQuestion;
        private int _turn = 0;

        /// <summary> </summary>
        /// <param name="triviaData"></param>
        /// <param name="channel"></param>
        /// <param name="turns"></param>
        public TriviaGame(
            IReadOnlyDictionary<string, string[]> triviaData,
            IMessageChannel channel,
            int turns)
        {
            _triviaData = new Stack<QA>(triviaData.Select(kv => new QA(kv.Key, kv.Value)).Shuffle(28));
            _channel = channel;
            _turns = turns;

            _questionTimer = new Timer(async _ =>
            {
                await _channel.SendMessageAsync("Time up.");
                if (!_triviaData.Any())
                {
                    await OutOfQuestions();
                }
                else
                {
                    await _channel.SendMessageAsync($"Next question commencing in 15 seconds.");
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    await AskQuestion();
                }
            },
            null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary> Starts the Trivia game. </summary>
        public async Task Start()
        {
            await _channel.SendMessageAsync("Starting trivia.");
            await AskQuestion();
        }

        /// <summary> Ends the Trivia game. </summary>
        public async Task End()
        {
            var sb = new StringBuilder("Game over. Final score: ```");
            foreach (var kv in _scoreboard)
            {
                sb.AppendLine($"{(await _channel.GetUserAsync(kv.Key)).Username}: {kv.Value} point(s).");
            }
            sb.Append("```");

            await _channel.SendMessageAsync(sb.ToString());
            await GameEnd(_channel.Id);
        }

        private async Task AskQuestion()
        {
            _currentQuestion = _triviaData.Pop();
            _turn++;
            _isAnswered.SetValue(false);
            await _channel.SendMessageAsync(_currentQuestion.Question);
            _questionTimer.Change(TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
        }

        private async Task OutOfQuestions()
        {
            var winner = (await _channel.GetUserAsync(_scoreboard.OrderByDescending(kv => kv.Value).First().Key)).Username;
            await _channel.SendMessageAsync($"Out of questions. **{winner}** has the most points.");
            await End();
        }

        internal async Task CheckTrivia(SocketMessage m)
        {
            var msg = m as SocketUserMessage;
            if (msg == null) return;

            if (_currentQuestion.Answers.Contains(msg.Content, StringComparer.OrdinalIgnoreCase) &&
                _isAnswered.TryUpdate(newValue: true, comparisonValue: false))
            {
                _questionTimer.Change(Timeout.Infinite, Timeout.Infinite);
                var userScore = _scoreboard.AddOrUpdate(msg.Author.Id, 1, (k, v) => ++v);
                await _channel.SendMessageAsync($"Correct. **{msg.Author.Username}** is now at **{userScore}** point(s).");

                if (_turn == _turns)
                {
                    await End();
                }
                else if (!_triviaData.Any())
                {
                    await OutOfQuestions();
                }
                else
                {
                    await _channel.SendMessageAsync("Next question commencing in 15 seconds.");
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    await AskQuestion();
                }
            }
        }

        internal event Func<ulong, Task> GameEnd;

        private sealed class QA
        {
            public string Question { get; }
            public string[] Answers { get; }
            public QA(string q, string[] a)
            {
                Question = q;
                Answers = a;
            }
        }

        private sealed class Atomic<T>
            where T : struct
        {
            private readonly object lockObj = new object();
            private T value;

            public Atomic(T initialValue)
            {
                value = initialValue;
            }

            public bool TryUpdate(T newValue, T comparisonValue)
            {
                lock (lockObj)
                {
                    var result = value.Equals(comparisonValue);
                    if (result)
                        value = newValue;

                    return result;
                }
            }

            public void SetValue(T newValue) => value = newValue;

            //public static implicit operator T(Atomic<T> atomic) => atomic.value;
        }
    }
}