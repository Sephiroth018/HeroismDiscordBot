﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using Discord.Commands;
using Discord.WebSocket;
using F23.StringSimilarity;
using F23.StringSimilarity.Interfaces;
using HeroismDiscordBot.Service.Common.Configuration;
using HeroismDiscordBot.Service.Discord;
using HeroismDiscordBot.Service.Discord.Commands;
using HeroismDiscordBot.Service.Discord.MessageHandlers;
using HeroismDiscordBot.Service.Entities.DAL;
using HeroismDiscordBot.Service.Logging;
using HeroismDiscordBot.Service.Processors;
using MoreLinq;
using NLog;
using NLog.Config;
using NLog.Targets;
using SimpleInjector;
using SimpleInjector.Diagnostics;
using SimpleInjector.Lifestyles;
using WowDotNetAPI;
using ILogger = HeroismDiscordBot.Service.Logging.ILogger;

namespace HeroismDiscordBot.Service
{
    public partial class Service : ServiceBase
    {
        private readonly Container _container;
        private readonly NLogProxy<Service> _logger = new NLogProxy<Service>();
        private List<IProcessorManager> _processorManagers;

        public Service()
        {
            InitializeComponent();
            _container = new Container();
            BuildContainer();
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                StartService();
            }
            catch (Exception e)
            {
                _logger.LogException(e);
                throw;
            }
        }

        private void BuildContainer()
        {
            var configuration = Configuration.LoadConfiguration();
            _container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();

            _container.RegisterConditional(typeof(ILogger), context => typeof(NLogProxy<>).MakeGenericType(context.Consumer.ImplementationType), Lifestyle.Singleton, context => true);
            _container.Register<IConfiguration>(() => configuration, Lifestyle.Singleton);
            _container.Register<IWoWClientConfiguration>(() => configuration.WoW, Lifestyle.Singleton);
            _container.Register<IDiscordConfiguration>(() => configuration.Discord, Lifestyle.Singleton);
            _container.Register<IDiscorMemberConfiguration>(() => configuration.Discord.MemberChangeConfiguration, Lifestyle.Singleton);
            _container.Register<IExplorer>(() =>
                                           {
                                               var config = _container.GetInstance<IWoWClientConfiguration>();
                                               return new WowExplorer(config.Region, config.Locale, config.ClientId);
                                           },
                                           Lifestyle.Scoped);
            _container.Register<IRepository, BotContext>(Lifestyle.Transient);
            _container.RegisterSingleton<Func<IRepository>>(() => _container.GetInstance<IRepository>());
            _container.RegisterCollection<IProcessor>(new[] { Assembly.GetExecutingAssembly() });
            _container.RegisterSingleton<IServiceProvider>(() => _container);
            _container.RegisterSingleton<IMetricStringDistance, Damerau>();
            _container.Register(typeof(IDiscordMessageBuilder<>), new[] { typeof(IDiscordMessageBuilder<>).Assembly }, Lifestyle.Singleton);
            _container.Register(typeof(IDiscordMessageSender<>), new[] { typeof(IDiscordMessageSender<>).Assembly }, Lifestyle.Singleton);


            _container.RegisterSingleton(() => new CommandService(new CommandServiceConfig
                                                                  {
                                                                      CaseSensitiveCommands = false,
                                                                      DefaultRunMode = RunMode.Async
                                                                  }));
            _container.RegisterSingleton<CommandHandler>();
            _container.RegisterSingleton(() => DiscordClientInitializer.Initialize(_container).Result);

            _container.GetRegistration(typeof(IRepository))
                      .Registration
                      .SuppressDiagnosticWarning(DiagnosticType.DisposableTransientComponent, "Handled by application code");

            _container.Verify();

            Target.Register<DiscordPrivateMessageTarget>("DiscordPrivateMessage");
            ConfigurationItemFactory.Default.CreateInstance = type => _container.GetRegistration(type)?.GetInstance() ?? Activator.CreateInstance(type);
        }

        public void StartService()
        {
            //CleanUp();
            var configuration = _container.GetInstance<IConfiguration>();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(configuration.Culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(configuration.Culture);

            //start discord command handler
            _container.GetInstance<CommandHandler>();

            _processorManagers = GetProcessorManagers();

            _processorManagers.AsParallel().ForEach(p => p.Start());
        }

        private List<IProcessorManager> GetProcessorManagers()
        {
            List<IProcessorManager> processors;
            using (AsyncScopedLifestyle.BeginScope(_container))
            {
                processors = _container.GetAllInstances<IProcessor>()
                                       .Select(p => typeof(ProcessorManager<>).MakeGenericType(p.GetType()))
                                       .Select(t => _container.GetInstance(t) as IProcessorManager)
                                       .ToList();
            }

            return processors;
        }

        // ReSharper disable once UnusedMember.Local
        private void CleanUp()
        {
            using (AsyncScopedLifestyle.BeginScope(_container))
            {
                //var configuration = _container.GetInstance<IConfiguration>();
                //var discordClient = _container.GetInstance<DiscordSocketClient>();
                //var discordGuild = discordClient.GetGuild(configuration.DiscordGuildId) as IGuild;
                //var channel = discordGuild.GetTextChannelAsync(configuration.DiscordMemberChangeChannelId).Result;
                //var messages = channel.GetMessagesAsync().Flatten().Result.ToList();

                //while (messages.Any())
                //{
                //    channel.DeleteMessagesAsync(messages.Select(m => m.Id)).Wait();
                //    messages = channel.GetMessagesAsync().Flatten().Result.ToList();
                //}

                using (var repository = _container.GetInstance<Func<IRepository>>().Invoke())
                {
                    repository.Players.RemoveRange(repository.Players);
                    repository.Characters.RemoveRange(repository.Characters);
                    repository.Events.RemoveRange(repository.Events);

                    repository.SaveChanges();
                }
            }
        }

        protected override void OnStop()
        {
            try
            {
                StopService();
            }
            catch (Exception e)
            {
                _logger.LogException(e);
                throw;
            }
        }

        private void StopService()
        {
            _processorManagers.AsParallel().ForEach(p => p.Stop());

            var discordClient = _container.GetInstance<DiscordSocketClient>();
            DiscordClientInitializer.Disconnect(discordClient).Wait();

            LogManager.Shutdown();
        }
    }
}