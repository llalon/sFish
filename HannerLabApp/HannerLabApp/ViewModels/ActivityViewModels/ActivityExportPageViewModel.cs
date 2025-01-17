﻿using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Autofac;
using HannerLabApp.Models;
using HannerLabApp.Services;
using HannerLabApp.Services.Exporters;
using HannerLabApp.Services.Managers;
using HannerLabApp.Services.Media;
using HannerLabApp.Utils;
using HannerLabApp.Views;
using Serilog;
using TinyMvvm;
using Xamarin.Essentials;
using Xamarin.Forms;


namespace HannerLabApp.ViewModels.ActivityViewModels   
{
    /// <summary>
    /// Main page where the user creates an activity and exports it along with all data.
    /// The flow:
    ///     - Invoke flow routine (ExportDataAsync()) from ExportToFileCommand/ Button
    ///     - Spawn an Activity Details page (SpawnActivityDetailsViewModelAsync()) on completion the model is sent via message bus
    ///     - Model is received back at this page via message bus (ReceiveCompletedActivityDetailsAsync())
    ///     - Export is generated based on the activity model (GenerateExportAsync()). On completion the file is shared to user via native share menu.
    ///
    /// </summary>
    public class ActivityExportPageViewModel : ViewModelBase
    {
        private readonly ILogger _logger;

        private readonly IActivityExportCreator _activityExportCreator;
        private readonly IPageService _pageService;
        private readonly IFileShare _fileShare;

        private const string DefaultExportMessage =
            "Export a copy of all the current projects data to an Excel Workbook. For more advanced queries of data utilize the raw application data. For more information see the help page.";

        private bool _isLoading = false;
        private bool _isIncludeAttachmentsSelected;
        private bool _isUseMdmaprFormatSelected;
        private bool _isSoftExportSelected;
        private string _exportMessage;

        public string ExportMessage 
        {
            get => _exportMessage;
            private set => Set(ref _exportMessage, value);
        }

        public bool IsIncludeAttachmentsSelected
        {
            get => _isIncludeAttachmentsSelected;
            set => Set(ref _isIncludeAttachmentsSelected, value);
        }

        public bool IsUseMdmaprFormatSelected
        {
            get => _isUseMdmaprFormatSelected;
            set => Set(ref _isUseMdmaprFormatSelected, value);
        }

        public bool IsSoftExportSelected
        {
            get => _isSoftExportSelected;
            set => Set(ref _isSoftExportSelected, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => Set(ref _isLoading, value);
        }

        public ICommand ExportToFileCommand { get; private set; }
        public ICommand ActivityHistoryCommand { get; private set; }
        public ActivityExportPageViewModel(ILogger logger, IPageService pageService, IFileShare fileShare, IActivityExportCreator activityExportCreator)
        {
            _logger = logger;

            _pageService = pageService;
            _activityExportCreator = activityExportCreator;
            _fileShare = fileShare;

            ExportToFileCommand = new TinyCommand(async () => await ExportDataAsync());
            ActivityHistoryCommand = new TinyCommand(async () => await _pageService.NavigateToAsync("ActivityHistoryListView", null));

            ExportMessage = DefaultExportMessage;
        }

        private async Task ExportDataAsync()
        {
            _logger.Information("Exporting data");

            try
            {

                // Prompt user to confirm before
                if (!IsSoftExportSelected)
                {
                    if (!await _pageService.ShowYesNoAlertAsync("Export Activity",
                            "Once exported, all exported samples, instrumental readings, observations, and photos will no longer be shown in the app. \n\nUse soft export to prevent these items from being tagged as exported.",
                            "Continue", "Cancel")) return;
                }

                // Alert user that MDMAPR does not support custom units.
                if (IsUseMdmaprFormatSelected)
                {
                    await _pageService.ShowAlertAsync("Warning!",
                        "You have selected to export using the MDMAPR2.0 format. MDMAPR does not support custom units. Please ensure you have recorded using the correct units, or convert the units after export. For more information visit the MDMAPR homepage.",
                        "I understand");
                }

                await SpawnActivityDetailsViewModelAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception during ExportDataAsync()");
            }
        }


        /// <summary>
        /// Opens up an ActivityDetailsView to enter details on the activity.
        /// Instead of saving to db like other DetailsViewModels, the ActivityDetailsViewModel will obtain the required information and pass it along to the exporter.
        /// </summary>
        /// <returns></returns>
        private async Task SpawnActivityDetailsViewModelAsync()
        {
            try
            {
                // Subscribe to the response for when the user completes the activity details
                MessagingCenter.Subscribe<ActivityManager, Activity>
                (this, MsgEvents.GetModel(typeof(Activity), MsgEvents.Event.Addition),
                    async (ActivityManager source, Activity parameter) => await ReceiveCompletedActivityDetailsAsync(source, parameter));

                var vm = App.AppContainer.Resolve<IValidableViewModel<Activity>>();
                var v = App.AppContainer.Resolve<IDetailsView<Activity>>();

                await _pageService.NavigateToAsync(v.GetType().Name, vm);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception during SpawnActivityDetailsViewModelAsync()");
            }
        }

        /// <summary>
        /// Triggered when an activity is completed through the details page
        /// </summary>
        /// <param name="source"></param>
        /// <param name="parameter"></param>
        private async Task ReceiveCompletedActivityDetailsAsync(ActivityManager source, Activity parameter)
        {
            try
            {
                // unsubscribe from message
                MessagingCenter.Unsubscribe<ActivityManager, Activity>(this,
                    MsgEvents.GetModel(typeof(Activity), MsgEvents.Event.Addition));

                await GenerateExportAsync(parameter);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception during ReceiveCompletedActivityDetailsAsync()");
            }
        }

        /// <summary>
        /// Given the received activity, generate the export and send to user for download 
        /// </summary>
        /// <param name="activity"></param>
        /// <returns></returns>
        private async Task GenerateExportAsync(Activity activity)
        {
            _logger.Information("Generating export data");

            IsLoading = true;

            // Generate an activity export package from the activity.
            ExportMessage = "Generating activity export";

            Export export = null;
            try
            {
                export = await _activityExportCreator.CreateActivityExportAsync(activity);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception during CreateActivityExportAsync() in GenerateExportAsync()");
                return;
            }

            if (export == null)
            {
                _logger.Error("Error during CreateActivityExportAsync() in GenerateExportAsync(). export is null.");
                return;
            }

            // If there isn't anything to export, ask user if they are sure.
            if (export?.Ednas?.Count + export?.Observations?.Count + export?.Readings?.Count + export?.Photos?.Count == 0)
            {
                if (!await _pageService.ShowYesNoAlertAsync("Warning!",
                        "Exported activity contains no samples, observations, or photos. Continue export anyway?",
                        "Continue", "Cancel"))
                {
                    IsLoading = false; 
                    return;
                }
            }

            // Generate export package file
            ExportMessage = "Generating export package";

            string exportFilePath = string.Empty;
            try
            {
                exportFilePath = await ActivityExportCreator.GenerateExportPackageAsync(export, IsIncludeAttachmentsSelected, IsUseMdmaprFormatSelected);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception during CreateActivityExportAsync() in GenerateExportPackageAsync()");
                return;
            }

            // Check for success
            if (string.IsNullOrEmpty(exportFilePath))
            {
                var ans = await _pageService.ShowYesNoAlertAsync("Error!", "Could not generate export package. Copy diagnostic data to clipboard instead?", "Yes", "No");
                
                if (ans)
                    await Clipboard.SetTextAsync(export.ToString());

                _logger.Error("Error during CreateActivityExportAsync() in GenerateExportPackageAsync(). exportFilePath is null or empty");

                IsLoading = false;
                return;
            }

#if DEBUG
            // For extracting file in debugger without having to go through adb
            var exportFileExtractDebug = Convert.ToBase64String(File.ReadAllBytes(exportFilePath));
            Console.WriteLine(exportFileExtractDebug);
#endif


            // Only tag samples as exported if export was successful and soft export is not enabled.
            // Only save to db if soft export is not enabled.
            ExportMessage = "Updating database";
            if (!IsSoftExportSelected)
            {
                try
                {
                    await _activityExportCreator.SaveExportAsync(export);
                }
                catch(Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception during CreateActivityExportAsync() in SaveExportAsync()");
                    return;
                }

                try
                {
                    await _activityExportCreator.TagItemsAsExportedAsync(export);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception during CreateActivityExportAsync() in TagItemsAsExportedAsync()");
                    return;
                }
            }


            // Finally Share the file to user
            ExportMessage = "Done";

            try
            {
                await _fileShare.ShareFileAsync(exportFilePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception during CreateActivityExportAsync() in ShareFileAsync()");
                return;
            }

            IsLoading = false;
            ExportMessage = DefaultExportMessage;
        }
    }
}
