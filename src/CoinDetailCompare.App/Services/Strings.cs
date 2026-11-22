using System.ComponentModel;

namespace CoinDetailCompare.App.Services;

/// <summary>
/// Bindable view over <see cref="LocalizationService"/>.
///
/// Every localization key is exposed as a named property so XAML can bind it
/// without indexer quotes (which are not allowed inside MarkupExtension):
///   Text="{Binding CaptureSection, Source={StaticResource Strings}}"
///
/// When the language changes, PropertyChanged is raised for every key,
/// so all bound labels update in place.
/// </summary>
public class Strings : INotifyPropertyChanged
{
    private readonly LocalizationService _loc;

    public Strings(LocalizationService loc)
    {
        _loc = loc;
    }

    public string this[string key] => _loc.T(key);

    public void SetLanguage(string lang)
    {
        _loc.SetLanguage(lang);
        foreach (var key in _loc.Keys)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // app
    public string AppTitle => this["app.title"];

    // capture
    public string CaptureSection => this["capture.section"];
    public string CaptureAlbum => this["capture.album"];
    public string CapturePage => this["capture.page"];
    public string CaptureOrder => this["capture.order"];
    public string CaptureRotation => this["capture.rotation"];
    public string CaptureShootObverse => this["capture.shoot_obverse"];
    public string CaptureShootReverse => this["capture.shoot_reverse"];
    public string CaptureLoadObverse => this["capture.load_obverse"];
    public string CaptureLoadReverse => this["capture.load_reverse"];
    public string CaptureSwapSides => this["capture.swap_sides"];
    public string CaptureBatchMode => this["capture.batch_mode"];
    public string CaptureBatchCoins => this["capture.batch_coins"];
    public string CaptureOpenCamera => this["capture.open_camera"];
    public string CaptureStartBatch => this["capture.start_batch"];
    public string CaptureFrame => this["capture.capture_frame"];
    public string CaptureCancelBatch => this["capture.cancel_batch"];
    public string CaptureSaveAll => this["capture.save_all"];
    public string CaptureObverse => this["capture.obverse"];
    public string CaptureReverse => this["capture.reverse"];
    public string CaptureCameraOffline => this["capture.camera_offline"];

    // compare
    public string CompareSection => this["compare.section"];
    public string CompareCurrent => this["compare.current"];
    public string CompareBase => this["compare.base"];
    public string CompareMatchObverse => this["compare.match_obverse"];
    public string CompareMatchReverse => this["compare.match_reverse"];
    public string CompareOrientation => this["compare.orientation"];
    public string CompareSearch => this["compare.search"];
    public string CompareRebuild => this["compare.rebuild"];
    public string CompareAnnotationHint => this["compare.annotation_hint"];

    // annotation
    public string AnnotationToolLine => this["annotation.tool_line"];
    public string AnnotationToolEllipse => this["annotation.tool_ellipse"];
    public string AnnotationToolArrow => this["annotation.tool_arrow"];
    public string AnnotationUndo => this["annotation.undo"];
    public string AnnotationClear => this["annotation.clear"];
    public string AnnotationSaveLayer => this["annotation.save_layer"];

    // actions
    public string ActionsSection => this["actions.section"];
    public string ActionsSwapSides => this["actions.swap_sides"];
    public string ActionsSaveMatch => this["actions.save_match"];
    public string ActionsDuplicate => this["actions.duplicate"];
    public string ActionsVariant => this["actions.variant"];
    public string ActionsAnomaly => this["actions.anomaly"];
    public string ActionsNotDetermined => this["actions.not_determined"];
    public string ActionsSaveCoin => this["actions.save_coin"];
    public string ActionsRebuildIndex => this["actions.rebuild_index"];
    public string ActionsAnomalyDesc => this["actions.anomaly_desc"];

    // numista
    public string NumistaSection => this["numista.section"];
    public string NumistaManualLink => this["numista.manual_link"];
    public string NumistaLinkExample => this["numista.link_example"];
    public string NumistaValidateLink => this["numista.validate_link"];
    public string NumistaSearchByApi => this["numista.search_by_api"];
    public string NumistaSearchPlaceholder => this["numista.search_placeholder"];
    public string NumistaSearch => this["numista.search"];
    public string NumistaApplyItem => this["numista.apply_item"];

    // coin
    public string CoinSection => this["coin.section"];
    public string CoinMintYear => this["coin.mint_year"];
    public string CoinMint => this["coin.mint"];
    public string CoinQuality => this["coin.quality"];
    public string CoinAnomalyDesc => this["coin.anomaly_desc"];
    public string CoinSaveAttributes => this["coin.save_attributes"];

    // type
    public string TypeSection => this["type.section"];
    public string TypeName => this["type.name"];
    public string TypeIssuer => this["type.issuer"];
    public string TypePeriod => this["type.period"];
    public string TypeValue => this["type.value"];
    public string TypeCurrency => this["type.currency"];
    public string TypeDiameter => this["type.diameter"];
    public string TypeYears => this["type.years"];
    public string TypeComposition => this["type.composition"];
    public string TypeMass => this["type.mass"];
    public string TypeThickness => this["type.thickness"];
    public string TypeShape => this["type.shape"];
    public string TypeTechnique => this["type.technique"];
    public string TypeOrientation => this["type.orientation"];
    public string TypeNumistaUrl => this["type.numista_url"];
    public string TypeLoadType => this["type.load_type"];
    public string TypeSaveType => this["type.save_type"];

    // anomaly
    public string AnomalyCheck => this["anomaly.check"];
    public string AnomalyDetected => this["anomaly.detected"];
    public string AnomalyOk => this["anomaly.ok"];
    public string AnomalyScore => this["anomaly.score"];

    // status
    public string StatusBackendOk => this["status.backend_ok"];
    public string StatusBackendDown => this["status.backend_down"];
    public string StatusSaved => this["status.saved"];
    public string StatusError => this["status.error"];
    public string StatusLanguage => this["status.language"];
}
