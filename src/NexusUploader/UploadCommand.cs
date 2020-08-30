using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexusUploader.Nexus.Services;
using Spectre.Cli;
using Spectre.Console;
using ValidationResult = Spectre.Cli.ValidationResult;

namespace NexusUploader.Nexus
{
    public class UploadCommand : AsyncCommand<UploadCommand.Settings>
    {
        private readonly ManageClient _manager;
        private readonly ApiClient _apiClient;
        private readonly UploadClient _uploader;
        private readonly ModConfiguration _config;
        private readonly ILogger<UploadCommand> _logger;

        public UploadCommand(ManageClient client, ApiClient apiClient, UploadClient uploader, ModConfiguration config, ILogger<UploadCommand> logger)
        {
            _manager = client;
            _apiClient = apiClient;
            _uploader = uploader;
            _config = config;
            _logger = logger;
        }
        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
        {
            var config = settings.MergedConfiguration;
            var fileOpts = new FileOptions(config.FileName, settings.FileVersion) {Description = config.FileDescription };
            if (!IsConfigurationValid(settings) && !settings.AllowInteractive) {
                AnsiConsole.MarkupLine("[bold red]ERROR[/]: not all configuration is set correctly and unex is not running interactively. Exiting!");
                return -1;
            }
            _logger.LogInformation($"Attempting to retrieve game details for '{config.Game}'");
            var gameId = await _apiClient.GetGameId(config.Game, config.ApiKey);
            _logger.LogInformation($"Game details loaded: {config.Game}/{gameId}");
            var game = new GameRef(config.Game, gameId);
            if (!string.IsNullOrWhiteSpace(config.PreviousFile)) {
                if (config.PreviousFile == "auto") {
                    _logger.LogInformation("Automatic file update detection enabled! Retrieving previous file versions");
                    var fileId = await _apiClient.GetLatestFileId(config.Game, config.ModId, config.ApiKey);
                    if (fileId.HasValue) {
                        _logger.LogInformation($"Uploaded file will replace existing file '{fileId.Value}");
                        fileOpts.PreviousFileId = fileId;
                    }
                } else {
                    if (int.TryParse(config.PreviousFile, out var previousId)) {
                        _logger.LogInformation($"Uploaded file will replace existing file '{previousId}");
                        fileOpts.PreviousFileId = previousId;
                    };
                }
            }
            _logger.LogInformation($"Preparing to upload '{settings.ModFilePath}' to Nexus Mods upload API");
            var upload = await _uploader.UploadFile(game, config.ModId, new System.IO.FileInfo(settings.ModFilePath));
            _logger.LogInformation($"File successfully uploaded to Nexus Mods with ID '{upload.Id}'");
            var available = await _uploader.CheckStatus(upload);
            _logger.LogDebug($"File '{upload.Id}' confirmed as assembled: {available}");
            _logger.LogInformation($"Adding uploaded file to mod {config.ModId}");
            _logger.LogDebug($"Using file options: {fileOpts.ToString()}");
            await _manager.AddFile(game, config.ModId, upload, fileOpts);
            _logger.LogInformation($"{upload.OriginalFile} successfully uploaded and added to mod {config.ModId}!");
            _logger.LogDebug("Now go ask @Pickysaurus when a real API will be available! ;)");
            return 0;
        }

        private bool IsConfigurationValid(Settings settings) {
            return true;
            //TODO: obviously
        }

        public class Settings : AppSettings {
            private readonly ModConfiguration _config;

            public ModConfiguration MergedConfiguration => _config;

            public Settings(ModConfiguration config)
            {
                _config = config;
            }
            /* [CommandOption("-k|--api-key [value]")]
            public FlagValue<string> ApiKey {get;set;} */

            [CommandArgument(0, "<mod-id>")]
            public int ModId {get;set;}

            [CommandArgument(1, "<archive-file>")]
            public string ModFilePath {get;set;}

            [CommandOption("-i|--interactive")]
            public bool AllowInteractive {get;set;}

            [CommandOption("-k|--api-key [keyValue]")]
            public FlagValue<string> ApiKey {get;set;}

            [CommandOption("-f|--file-name [value]")]
            public FlagValue<string> FileName {get;set;}

            [CommandOption("-v|--version <value>")]
            [Required]
            public string FileVersion {get;set;}

            private bool IsSettingsValid() {
                return ModFilePath.IsSet()
                    && (ApiKey.IsSet || _config.ApiKey.IsSet()) 
                    && (FileName.IsSet || _config.FileName.IsSet())
                    && ModId != default(int)
                    && _config.Game.IsSet();
            }

            public override ValidationResult Validate()
            {
                if (!IsSettingsValid() && !AllowInteractive) {
                    return ValidationResult.Error("Not all required settings provided and unex is not running interactively!");
                } else if (AllowInteractive) {
                    //prompt here
                    return ValidationResult.Error("Interactive execution not currently implemented. Sorry about that!");
                } else if (IsSettingsValid()) {
                    _config.ApiKey = ApiKey.IsSet ? ApiKey.Value : _config.ApiKey;
                    _config.FileDescription = _config.FileDescription ?? string.Empty;
                    _config.FileName = FileName.IsSet ? FileName.Value : _config.FileName;
                    _config.ModId = ModId;
                }
                return base.Validate();
            }
        }
    }
}