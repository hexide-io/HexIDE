using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Antlr4.Runtime;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

public partial class VB6BuiltIns
{
    private readonly IBasicStandardLibrary stdLib;

    /// <summary>
    /// The application name <c>MsgBox</c> / <c>InputBox</c> put in the caption when the caller omits the
    /// Title argument — <c>App.Title</c>. Set by the interpreter that owns these builtins.
    /// </summary>
    /// <remarks>
    /// Returns null when there is no project behind the program (the test harness, the bare interpreter):
    /// an omitted title then stays omitted all the way to the host, which supplies its own last-resort
    /// caption. Substituting an empty string here instead would look like a deliberately blank title and
    /// lose the distinction #131 exists to keep.
    /// </remarks>
    public Func<string?>? AppTitle { get; set; }

    private static Dictionary<string, Vb6Value> builtInConstants = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase)
    {
        ["vb3DDKShadow"] = -2147483627,
        ["vb3DFace"] = -2147483633,
        ["vb3DHighlight"] = -2147483628,
        ["vb3DLight"] = -2147483626,
        ["vb3DShadow"] = -2147483632,
        ["vbActiveBorder"] = -2147483638,
        ["vbActiveTitleBar"] = -2147483646,
        ["vbActiveTitleBarText"] = -2147483639,
        ["vbAddNew"] = 2,
        ["vbAlignBottom"] = 2,
        ["vbAlignLeft"] = 3,
        ["vbAlignNone"] = 0,
        ["vbAlignRight"] = 4,
        ["vbAlignTop"] = 1,
        ["vbAltMask"] = 4,
        ["vbAppTaskManager"] = 3,
        ["vbAppWindows"] = 2,
        ["vbApplicationWorkspace"] = -2147483636,
        ["vbArrangeIcons"] = 3,
        ["vbArrow"] = 1,
        ["vbArrowHourglass"] = 13,
        ["vbArrowQuestion"] = 14,
        ["vbAsyncReadForceUpdate"] = 16,
        ["vbAsyncReadGetFromCacheIfNetFail"] = 524288,
        ["vbAsyncReadOfflineOperation"] = 8,
        ["vbAsyncReadResynchronize"] = 512,
        ["vbAsyncReadSynchronousDownload"] = 1,
        ["vbAsyncStatusCodeBeginDownloadData"] = 4,
        ["vbAsyncStatusCodeBeginSyncOperation"] = 15,
        ["vbAsyncStatusCodeCacheFileNameAvailable"] = 14,
        ["vbAsyncStatusCodeConnecting"] = 2,
        ["vbAsyncStatusCodeDownloadingData"] = 5,
        ["vbAsyncStatusCodeEndDownloadData"] = 6,
        ["vbAsyncStatusCodeEndSyncOperation"] = 16,
        ["vbAsyncStatusCodeError"] = 0,
        ["vbAsyncStatusCodeFindingResource"] = 1,
        ["vbAsyncStatusCodeMIMETypeAvailable"] = 13,
        ["vbAsyncStatusCodeRedirecting"] = 3,
        ["vbAsyncStatusCodeSendingRequest"] = 11,
        ["vbAsyncStatusCodeUsingCachedCopy"] = 10,
        ["vbAsyncTypeByteArray"] = 2,
        ["vbAsyncTypeFile"] = 1,
        ["vbAsyncTypePicture"] = 0,
        ["vbAutomatic"] = 1,
        ["vbBOF"] = 1,
        ["vbBSDash"] = 2,
        ["vbBSDashDot"] = 4,
        ["vbBSDashDotDot"] = 5,
        ["vbBSDot"] = 3,
        ["vbBSInsideSolid"] = 6,
        ["vbBSNone"] = 0,
        ["vbBSSolid"] = 1,
        ["vbBeginDrag"] = 1,
        ["vbBlack"] = 0,
        ["vbBlackness"] = 1,
        ["vbBlue"] = 16711680,
        ["vbBoth"] = 3,
        ["vbBringToFront"] = 0,
        ["vbButtonFace"] = -2147483633,
        ["vbButtonGraphical"] = 1,
        ["vbButtonShadow"] = -2147483632,
        ["vbButtonStandard"] = 0,
        ["vbButtonText"] = -2147483630,
        ["vbCFBitmap"] = 2,
        ["vbCFDIB"] = 8,
        ["vbCFEMetafile"] = 14,
        ["vbCFFiles"] = 15,
        ["vbCFLink"] = -16640,
        ["vbCFMetafile"] = 3,
        ["vbCFPalette"] = 9,
        ["vbCFRTF"] = -16639,
        ["vbCFText"] = 1,
        // `vbCancel` is DECLARED TWICE by VB6 itself, and this table can only hold one of them:
        // VBRUN.DragConstants.vbCancel = 0 and VBA.VbMsgBoxResult.vbCancel = 2. It is the only one of
        // the 728 in-box constants that is ambiguous by library (see docs/vb6-inbox-constants.md).
        //
        // Measured: a BARE `vbCancel` is 2 — default reference order gives VBA precedence — so the
        // VbMsgBoxResult entry below is the right one to keep, and the DragConstants 0 that used to sit
        // here is removed. It was not overriding anything: C# indexer-style collection initialization
        // does not throw on a duplicate key, the later assignment simply wins, so the value was being
        // decided by declaration order and happened to land on the correct one.
        //
        // `VBRUN.vbCancel` and `VBRUN.DragConstants.vbCancel` are 0 in VB6 and this table still answers
        // 2 for them, because the library and enum qualifiers are resolved transparently. That is a
        // known wrong value and needs the structured, library-aware model, not another table entry.
        ["vbCascade"] = 0,
        ["vbCenter"] = 2,
        ["vbCentimeters"] = 7,
        ["vbCharacters"] = 4,
        ["vbChecked"] = 1,
        ["vbCold"] = 2,
        ["vbComboDropdown"] = 0,
        ["vbComboDropdownList"] = 2,
        ["vbComboSimple"] = 1,
        ["vbContainerPosition"] = 9,
        ["vbContainerSize"] = 10,
        ["vbCopyPen"] = 13,
        ["vbCross"] = 6,
        ["vbCrosshair"] = 2,
        ["vbCtrlMask"] = 2,
        ["vbCustom"] = 99,
        ["vbCyan"] = 16776960,
        ["vbDDESourceClosed"] = 6,
        ["vbDash"] = 1,
        ["vbDashDot"] = 3,
        ["vbDashDotDot"] = 4,
        ["vbDataActionAddNew"] = 5,
        ["vbDataActionBookmark"] = 9,
        ["vbDataActionCancel"] = 0,
        ["vbDataActionClose"] = 10,
        ["vbDataActionDelete"] = 7,
        ["vbDataActionFind"] = 8,
        ["vbDataActionMoveFirst"] = 1,
        ["vbDataActionMoveLast"] = 4,
        ["vbDataActionMoveNext"] = 3,
        ["vbDataActionMovePrevious"] = 2,
        ["vbDataActionUnload"] = 11,
        ["vbDataActionUpdate"] = 6,
        ["vbDataErrContinue"] = 0,
        ["vbDataErrDisplay"] = 1,
        ["vbDataTransferFailed"] = 8,
        ["vbDefault"] = 0,
        ["vbDesktop"] = -2147483647,
        ["vbDiagonalCross"] = 7,
        ["vbDot"] = 2,
        ["vbDownwardDiagonal"] = 5,
        ["vbDropEffectCopy"] = 1,
        ["vbDropEffectMove"] = 2,
        ["vbDropEffectNone"] = 0,
        ["vbDropEffectScroll"] = -2147483648,
        ["vbDstInvert"] = 5570569,
        ["vbEOF"] = 1,
        ["vbEndDrag"] = 2,
        ["vbEnter"] = 0,
        ["vbExtender"] = 1,
        ["vbFSSolid"] = 0,
        ["vbFSTransparent"] = 1,
        ["vbFixedDialog"] = 3,
        ["vbFixedDouble"] = 3,
        ["vbFixedSingle"] = 1,
        ["vbFixedToolWindow"] = 4,
        ["vbFormCode"] = 1,
        ["vbFormControlMenu"] = 0,
        ["vbFormMDIForm"] = 4,
        ["vbFormOwner"] = 5,
        ["vbGrayText"] = -2147483631,
        ["vbGrayed"] = 2,
        ["vbGreen"] = 65280,
        ["vbHighlight"] = -2147483635,
        ["vbHighlightText"] = -2147483634,
        ["vbHimetric"] = 8,
        ["vbHitResultClose"] = 2,
        ["vbHitResultHit"] = 3,
        ["vbHitResultOutside"] = 0,
        ["vbHitResultTransparent"] = 1,
        ["vbHorizontal"] = 1,
        ["vbHorizontalLine"] = 2,
        ["vbHot"] = 1,
        ["vbHourglass"] = 11,
        ["vbIbeam"] = 3,
        ["vbIconPointer"] = 4,
        ["vbInactiveBorder"] = -2147483637,
        ["vbInactiveCaptionText"] = -2147483629,
        ["vbInactiveTitleBar"] = -2147483645,
        ["vbInactiveTitleBarText"] = -2147483629,
        ["vbInches"] = 5,
        ["vbInfoBackground"] = -2147483624,
        ["vbInfoText"] = -2147483625,
        ["vbInsideSolid"] = 6,
        ["vbInvert"] = 6,
        ["vbInvisible"] = 5,
        ["vbKey0"] = 48,
        ["vbKey1"] = 49,
        ["vbKey2"] = 50,
        ["vbKey3"] = 51,
        ["vbKey4"] = 52,
        ["vbKey5"] = 53,
        ["vbKey6"] = 54,
        ["vbKey7"] = 55,
        ["vbKey8"] = 56,
        ["vbKey9"] = 57,
        ["vbKeyA"] = 65,
        ["vbKeyAdd"] = 107,
        ["vbKeyB"] = 66,
        ["vbKeyBack"] = 8,
        ["vbKeyC"] = 67,
        ["vbKeyCancel"] = 3,
        ["vbKeyCapital"] = 20,
        ["vbKeyClear"] = 12,
        ["vbKeyControl"] = 17,
        ["vbKeyD"] = 68,
        ["vbKeyDecimal"] = 110,
        ["vbKeyDelete"] = 46,
        ["vbKeyDivide"] = 111,
        ["vbKeyDown"] = 40,
        ["vbKeyE"] = 69,
        ["vbKeyEnd"] = 35,
        ["vbKeyEscape"] = 27,
        ["vbKeyExecute"] = 43,
        ["vbKeyF"] = 70,
        ["vbKeyF1"] = 112,
        ["vbKeyF10"] = 121,
        ["vbKeyF11"] = 122,
        ["vbKeyF12"] = 123,
        ["vbKeyF13"] = 124,
        ["vbKeyF14"] = 125,
        ["vbKeyF15"] = 126,
        ["vbKeyF16"] = 127,
        ["vbKeyF2"] = 113,
        ["vbKeyF3"] = 114,
        ["vbKeyF4"] = 115,
        ["vbKeyF5"] = 116,
        ["vbKeyF6"] = 117,
        ["vbKeyF7"] = 118,
        ["vbKeyF8"] = 119,
        ["vbKeyF9"] = 120,
        ["vbKeyG"] = 71,
        ["vbKeyH"] = 72,
        ["vbKeyHelp"] = 47,
        ["vbKeyHome"] = 36,
        ["vbKeyI"] = 73,
        ["vbKeyInsert"] = 45,
        ["vbKeyJ"] = 74,
        ["vbKeyK"] = 75,
        ["vbKeyL"] = 76,
        ["vbKeyLButton"] = 1,
        ["vbKeyLeft"] = 37,
        ["vbKeyM"] = 77,
        ["vbKeyMButton"] = 4,
        ["vbKeyMenu"] = 18,
        ["vbKeyMultiply"] = 106,
        ["vbKeyN"] = 78,
        ["vbKeyNumlock"] = 144,
        ["vbKeyNumpad0"] = 96,
        ["vbKeyNumpad1"] = 97,
        ["vbKeyNumpad2"] = 98,
        ["vbKeyNumpad3"] = 99,
        ["vbKeyNumpad4"] = 100,
        ["vbKeyNumpad5"] = 101,
        ["vbKeyNumpad6"] = 102,
        ["vbKeyNumpad7"] = 103,
        ["vbKeyNumpad8"] = 104,
        ["vbKeyNumpad9"] = 105,
        ["vbKeyO"] = 79,
        ["vbKeyP"] = 80,
        ["vbKeyPageDown"] = 34,
        ["vbKeyPageUp"] = 33,
        ["vbKeyPause"] = 19,
        ["vbKeyPrint"] = 42,
        ["vbKeyQ"] = 81,
        ["vbKeyR"] = 82,
        ["vbKeyRButton"] = 2,
        ["vbKeyReturn"] = 13,
        ["vbKeyRight"] = 39,
        ["vbKeyS"] = 83,
        ["vbKeyScrollLock"] = 145,
        ["vbKeySelect"] = 41,
        ["vbKeySeparator"] = 108,
        ["vbKeyShift"] = 16,
        ["vbKeySnapshot"] = 44,
        ["vbKeySpace"] = 32,
        ["vbKeySubtract"] = 109,
        ["vbKeyT"] = 84,
        ["vbKeyTab"] = 9,
        ["vbKeyU"] = 85,
        ["vbKeyUp"] = 38,
        ["vbKeyV"] = 86,
        ["vbKeyW"] = 87,
        ["vbKeyX"] = 88,
        ["vbKeyY"] = 89,
        ["vbKeyZ"] = 90,
        ["vbLPColor"] = 3,
        ["vbLPCustom"] = 4,
        ["vbLPDefault"] = 0,
        ["vbLPLarge"] = 1,
        ["vbLPLargeShell"] = 3,
        ["vbLPMonochrome"] = 1,
        ["vbLPSmall"] = 0,
        ["vbLPSmallShell"] = 2,
        ["vbLPVGAColor"] = 2,
        ["vbLeave"] = 1,
        ["vbLeftButton"] = 1,
        ["vbLeftJustify"] = 0,
        ["vbLinkAutomatic"] = 1,
        ["vbLinkManual"] = 2,
        ["vbLinkNone"] = 0,
        ["vbLinkNotify"] = 3,
        ["vbLinkSource"] = 1,
        ["vbListBoxCheckbox"] = 1,
        ["vbListBoxStandard"] = 0,
        ["vbLogAuto"] = 0,
        ["vbLogEventTypeError"] = 1,
        ["vbLogEventTypeInformation"] = 4,
        ["vbLogEventTypeWarning"] = 2,
        ["vbLogOff"] = 1,
        ["vbLogOverwrite"] = 16,
        ["vbLogThreadID"] = 32,
        ["vbLogToFile"] = 2,
        ["vbLogToNT"] = 3,
        ["vbMagenta"] = 16711935,
        ["vbManual"] = 0,
        ["vbMaskNotPen"] = 3,
        ["vbMaskPen"] = 9,
        ["vbMaskPenNot"] = 5,
        ["vbMaximized"] = 2,
        ["vbMenuAccelAltBksp"] = 75,
        ["vbMenuAccelCtrlA"] = 1,
        ["vbMenuAccelCtrlB"] = 2,
        ["vbMenuAccelCtrlC"] = 3,
        ["vbMenuAccelCtrlD"] = 4,
        ["vbMenuAccelCtrlE"] = 5,
        ["vbMenuAccelCtrlF"] = 6,
        ["vbMenuAccelCtrlF1"] = 38,
        ["vbMenuAccelCtrlF11"] = 47,
        ["vbMenuAccelCtrlF12"] = 48,
        ["vbMenuAccelCtrlF2"] = 39,
        ["vbMenuAccelCtrlF3"] = 40,
        ["vbMenuAccelCtrlF4"] = 41,
        ["vbMenuAccelCtrlF5"] = 42,
        ["vbMenuAccelCtrlF6"] = 43,
        ["vbMenuAccelCtrlF7"] = 44,
        ["vbMenuAccelCtrlF8"] = 45,
        ["vbMenuAccelCtrlF9"] = 46,
        ["vbMenuAccelCtrlG"] = 7,
        ["vbMenuAccelCtrlH"] = 8,
        ["vbMenuAccelCtrlI"] = 9,
        ["vbMenuAccelCtrlIns"] = 71,
        ["vbMenuAccelCtrlJ"] = 10,
        ["vbMenuAccelCtrlK"] = 11,
        ["vbMenuAccelCtrlL"] = 12,
        ["vbMenuAccelCtrlM"] = 13,
        ["vbMenuAccelCtrlN"] = 14,
        ["vbMenuAccelCtrlO"] = 15,
        ["vbMenuAccelCtrlP"] = 16,
        ["vbMenuAccelCtrlQ"] = 17,
        ["vbMenuAccelCtrlR"] = 18,
        ["vbMenuAccelCtrlS"] = 19,
        ["vbMenuAccelCtrlT"] = 20,
        ["vbMenuAccelCtrlU"] = 21,
        ["vbMenuAccelCtrlV"] = 22,
        ["vbMenuAccelCtrlW"] = 23,
        ["vbMenuAccelCtrlX"] = 24,
        ["vbMenuAccelCtrlY"] = 25,
        ["vbMenuAccelCtrlZ"] = 26,
        ["vbMenuAccelDel"] = 73,
        ["vbMenuAccelF1"] = 27,
        ["vbMenuAccelF11"] = 36,
        ["vbMenuAccelF12"] = 37,
        ["vbMenuAccelF2"] = 28,
        ["vbMenuAccelF3"] = 29,
        ["vbMenuAccelF4"] = 30,
        ["vbMenuAccelF5"] = 31,
        ["vbMenuAccelF6"] = 32,
        ["vbMenuAccelF7"] = 33,
        ["vbMenuAccelF8"] = 34,
        ["vbMenuAccelF9"] = 35,
        ["vbMenuAccelShfitF2"] = 50,
        ["vbMenuAccelShiftCtrlF1"] = 60,
        ["vbMenuAccelShiftCtrlF11"] = 69,
        ["vbMenuAccelShiftCtrlF12"] = 70,
        ["vbMenuAccelShiftCtrlF2"] = 61,
        ["vbMenuAccelShiftCtrlF3"] = 62,
        ["vbMenuAccelShiftCtrlF4"] = 63,
        ["vbMenuAccelShiftCtrlF5"] = 64,
        ["vbMenuAccelShiftCtrlF6"] = 65,
        ["vbMenuAccelShiftCtrlF7"] = 66,
        ["vbMenuAccelShiftCtrlF8"] = 67,
        ["vbMenuAccelShiftCtrlF9"] = 68,
        ["vbMenuAccelShiftDel"] = 74,
        ["vbMenuAccelShiftF1"] = 49,
        ["vbMenuAccelShiftF11"] = 58,
        ["vbMenuAccelShiftF12"] = 59,
        ["vbMenuAccelShiftF3"] = 51,
        ["vbMenuAccelShiftF4"] = 52,
        ["vbMenuAccelShiftF5"] = 53,
        ["vbMenuAccelShiftF6"] = 54,
        ["vbMenuAccelShiftF7"] = 55,
        ["vbMenuAccelShiftF8"] = 56,
        ["vbMenuAccelShiftF9"] = 57,
        ["vbMenuAccelShiftIns"] = 72,
        ["vbMenuBar"] = -2147483644,
        ["vbMenuText"] = -2147483641,
        ["vbMergeCopy"] = 12583114,
        ["vbMergeNotPen"] = 12,
        ["vbMergePaint"] = 12255782,
        ["vbMergePen"] = 15,
        ["vbMergePenNot"] = 14,
        ["vbMiddleButton"] = 4,
        ["vbMillimeters"] = 6,
        ["vbMinimized"] = 1,
        ["vbModal"] = 1,
        ["vbModeless"] = 0,
        ["vbMoveFirst"] = 0,
        ["vbMoveLast"] = 0,
        ["vbMultiSelectExtended"] = 2,
        ["vbMultiSelectNone"] = 0,
        ["vbMultiSelectSimple"] = 1,
        ["vbNoDrop"] = 12,
        ["vbNoExtender"] = 0,
        ["vbNop"] = 11,
        ["vbNormal"] = 0,
        ["vbNotCopyPen"] = 4,
        ["vbNotMaskPen"] = 8,
        ["vbNotMergePen"] = 2,
        ["vbNotSrcCopy"] = 3342344,
        ["vbNotSrcErase"] = 1114278,
        ["vbNotXorPen"] = 10,
        ["vbOLEActivateAuto"] = 3,
        ["vbOLEActivateDoubleclick"] = 2,
        ["vbOLEActivateGetFocus"] = 1,
        ["vbOLEActivateManual"] = 0,
        ["vbOLEAutomatic"] = 0,
        ["vbOLEChanged"] = 0,
        ["vbOLEClosed"] = 2,
        ["vbOLEDiscardUndoState"] = -6,
        ["vbOLEDisplayContent"] = 0,
        ["vbOLEDisplayIcon"] = 1,
        ["vbOLEDragAutomatic"] = 1,
        ["vbOLEDragManual"] = 0,
        ["vbOLEDropAutomatic"] = 2,
        ["vbOLEDropManual"] = 1,
        ["vbOLEDropNone"] = 0,
        ["vbOLEEither"] = 2,
        ["vbOLEEmbedded"] = 1,
        ["vbOLEFlagChecked"] = 8,
        ["vbOLEFlagDisabled"] = 2,
        ["vbOLEFlagGrayed"] = 1,
        ["vbOLEFlagSeparator"] = 2048,
        ["vbOLEFrozen"] = 1,
        ["vbOLEHide"] = -3,
        ["vbOLEInPlaceActivate"] = -5,
        ["vbOLELinked"] = 0,
        ["vbOLEManual"] = 2,
        ["vbOLEMiscFlagDisableInPlace"] = 2,
        ["vbOLEMiscFlagMemStorage"] = 1,
        ["vbOLENone"] = 3,
        ["vbOLEOpen"] = -2,
        ["vbOLEPrimary"] = 0,
        ["vbOLERenamed"] = 3,
        ["vbOLESaved"] = 1,
        ["vbOLEShow"] = -1,
        ["vbOLESizeAutoSize"] = 2,
        ["vbOLESizeClip"] = 0,
        ["vbOLESizeStretch"] = 1,
        ["vbOLESizeZoom"] = 3,
        ["vbOLEUIActivate"] = -4,
        ["vbOver"] = 2,
        ["vbPRBNAuto"] = 7,
        ["vbPRBNCassette"] = 14,
        ["vbPRBNEnvManual"] = 6,
        ["vbPRBNEnvelope"] = 5,
        ["vbPRBNLargeCapacity"] = 11,
        ["vbPRBNLargeFmt"] = 10,
        ["vbPRBNLower"] = 2,
        ["vbPRBNManual"] = 4,
        ["vbPRBNMiddle"] = 3,
        ["vbPRBNSmallFmt"] = 9,
        ["vbPRBNTractor"] = 8,
        ["vbPRBNUpper"] = 1,
        ["vbPRCMColor"] = 2,
        ["vbPRCMMonochrome"] = 1,
        ["vbPRDPHorizontal"] = 2,
        ["vbPRDPSimplex"] = 1,
        ["vbPRDPVertical"] = 3,
        ["vbPRORLandscape"] = 2,
        ["vbPRORPortrait"] = 1,
        ["vbPRPQDraft"] = -1,
        ["vbPRPQHigh"] = -4,
        ["vbPRPQLow"] = -2,
        ["vbPRPQMedium"] = -3,
        ["vbPRPS10x14"] = 16,
        ["vbPRPS11x17"] = 17,
        ["vbPRPSA3"] = 8,
        ["vbPRPSA4"] = 9,
        ["vbPRPSA4Small"] = 10,
        ["vbPRPSA5"] = 11,
        ["vbPRPSB4"] = 12,
        ["vbPRPSB5"] = 13,
        ["vbPRPSCSheet"] = 24,
        ["vbPRPSDSheet"] = 25,
        ["vbPRPSESheet"] = 26,
        ["vbPRPSEnv10"] = 20,
        ["vbPRPSEnv11"] = 21,
        ["vbPRPSEnv12"] = 22,
        ["vbPRPSEnv14"] = 23,
        ["vbPRPSEnv9"] = 19,
        ["vbPRPSEnvB4"] = 33,
        ["vbPRPSEnvB5"] = 34,
        ["vbPRPSEnvB6"] = 35,
        ["vbPRPSEnvC3"] = 29,
        ["vbPRPSEnvC4"] = 30,
        ["vbPRPSEnvC5"] = 28,
        ["vbPRPSEnvC6"] = 31,
        ["vbPRPSEnvC65"] = 32,
        ["vbPRPSEnvDL"] = 27,
        ["vbPRPSEnvItaly"] = 36,
        ["vbPRPSEnvMonarch"] = 37,
        ["vbPRPSEnvPersonal"] = 38,
        ["vbPRPSExecutive"] = 7,
        ["vbPRPSFanfoldLglGerman"] = 41,
        ["vbPRPSFanfoldStdGerman"] = 40,
        ["vbPRPSFanfoldUS"] = 39,
        ["vbPRPSFolio"] = 14,
        ["vbPRPSLedger"] = 4,
        ["vbPRPSLegal"] = 5,
        ["vbPRPSLetter"] = 1,
        ["vbPRPSLetterSmall"] = 2,
        ["vbPRPSNote"] = 18,
        ["vbPRPSQuarto"] = 15,
        ["vbPRPSStatement"] = 6,
        ["vbPRPSTabloid"] = 3,
        ["vbPRPSUser"] = 256,
        ["vbPaletteModeContainer"] = 3,
        ["vbPaletteModeCustom"] = 2,
        ["vbPaletteModeHalftone"] = 0,
        ["vbPaletteModeNone"] = 4,
        ["vbPaletteModeObject"] = 5,
        ["vbPaletteModeUseZOrder"] = 1,
        ["vbPatCopy"] = 15728673,
        ["vbPatInvert"] = 5898313,
        ["vbPatPaint"] = 16452105,
        ["vbPicTypeBitmap"] = 1,
        ["vbPicTypeEMetafile"] = 4,
        ["vbPicTypeIcon"] = 3,
        ["vbPicTypeMetafile"] = 2,
        ["vbPicTypeNone"] = 0,
        ["vbPixels"] = 3,
        ["vbPoints"] = 2,
        ["vbPopupMenuCenterAlign"] = 4,
        ["vbPopupMenuLeftAlign"] = 0,
        ["vbPopupMenuLeftButton"] = 0,
        ["vbPopupMenuRightAlign"] = 8,
        ["vbPopupMenuRightButton"] = 2,
        ["vbRSTypeDynaset"] = 1,
        ["vbRSTypeSnapShot"] = 2,
        ["vbRSTypeTable"] = 0,
        ["vbRed"] = 255,
        ["vbResBitmap"] = 0,
        ["vbResCursor"] = 2,
        ["vbResIcon"] = 1,
        ["vbRightButton"] = 2,
        ["vbRightJustify"] = 1,
        ["vbSBNone"] = 0,
        ["vbSModeAutomation"] = 1,
        ["vbSModeStandalone"] = 0,
        ["vbScrollBars"] = -2147483648,
        ["vbSendToBack"] = 1,
        ["vbServer"] = 1,
        ["vbShapeCircle"] = 3,
        ["vbShapeOval"] = 2,
        ["vbShapeRectangle"] = 0,
        ["vbShapeRoundedRectangle"] = 4,
        ["vbShapeRoundedSquare"] = 5,
        ["vbShapeSquare"] = 1,
        ["vbShiftMask"] = 1,
        ["vbSizable"] = 2,
        ["vbSizableToolWindow"] = 5,
        ["vbSizeAll"] = 15,
        ["vbSizeNESW"] = 6,
        ["vbSizeNS"] = 7,
        ["vbSizeNWSE"] = 8,
        ["vbSizePointer"] = 5,
        ["vbSizeWE"] = 9,
        ["vbSolid"] = 0,
        ["vbSrcAnd"] = 8913094,
        ["vbSrcCopy"] = 13369376,
        ["vbSrcErase"] = 4457256,
        ["vbSrcInvert"] = 6684742,
        ["vbSrcPaint"] = 15597702,
        ["vbStartUpManual"] = 0,
        ["vbStartUpOwner"] = 1,
        ["vbStartUpScreen"] = 2,
        ["vbStartUpWindowsDefault"] = 3,
        ["vbTileHorizontal"] = 1,
        ["vbTileVertical"] = 2,
        ["vbTitleBarText"] = -2147483639,
        ["vbTooManyLinks"] = 7,
        ["vbTransparent"] = 0,
        ["vbTwips"] = 1,
        ["vbUnchecked"] = 0,
        ["vbUpArrow"] = 10,
        ["vbUpwardDiagonal"] = 4,
        ["vbUseDefaultCursor"] = 0,
        ["vbUseODBCCursor"] = 1,
        ["vbUseServersideCursor"] = 2,
        ["vbUser"] = 0,
        ["vbVCurrency"] = 6,
        ["vbVDate"] = 7,
        ["vbVDouble"] = 5,
        ["vbVEmpty"] = 0,
        ["vbVInteger"] = 2,
        ["vbVLong"] = 3,
        ["vbVNull"] = 1,
        ["vbVSingle"] = 4,
        ["vbVString"] = 8,
        ["vbVertical"] = 2,
        ["vbVerticalLine"] = 3,
        ["vbWhite"] = 16777215,
        ["vbWhiteness"] = 16,
        ["vbWindowBackground"] = -2147483643,
        ["vbWindowFrame"] = -2147483642,
        ["vbWindowText"] = -2147483640,
        ["vbWrongFormat"] = 1,
        ["vbXorPen"] = 7,
        ["vbYellow"] = 65535,
        ["VbGet"] = 2,
        ["VbLet"] = 4,
        ["VbMethod"] = 1,
        ["VbSet"] = 8,
        ["vbAbort"] = 3,
        ["vbAbortRetryIgnore"] = 2,
        ["vbAlias"] = 64,
        ["vbApplicationModal"] = 0,
        ["vbArchive"] = 32,
        ["vbArray"] = 8192,
        ["vbBinaryCompare"] = 0,
        ["vbBoolean"] = 11,
        ["vbByte"] = 17,
        ["vbCalGreg"] = 0,
        ["vbCalHijri"] = 1,
        ["vbCancel"] = 2,
        ["vbCritical"] = 16,
        ["vbCurrency"] = 6,
        ["vbDataObject"] = 13,
        ["vbDatabaseCompare"] = 2,
        ["vbDate"] = 7,
        ["vbDecimal"] = 14,
        ["vbDefaultButton1"] = 0,
        ["vbDefaultButton2"] = 256,
        ["vbDefaultButton3"] = 512,
        ["vbDefaultButton4"] = 768,
        ["vbDirectory"] = 16,
        ["vbDouble"] = 5,
        ["vbEmpty"] = 0,
        ["vbError"] = 10,
        ["vbExclamation"] = 48,
        ["vbFalse"] = 0,
        ["vbFirstFourDays"] = 2,
        ["vbFirstFullWeek"] = 3,
        ["vbFirstJan1"] = 1,
        ["vbFriday"] = 6,
        ["vbFromUnicode"] = 128,
        ["vbGeneralDate"] = 0,
        ["vbHidden"] = 2,
        ["vbHide"] = 0,
        ["vbHiragana"] = 32,
        ["vbIMEAlphaDbl"] = 7,
        ["vbIMEAlphaSng"] = 8,
        ["vbIMEDisable"] = 3,
        ["vbIMEHiragana"] = 4,
        ["vbIMEKatakanaDbl"] = 5,
        ["vbIMEKatakanaSng"] = 6,
        ["vbIMEModeAlpha"] = 8,
        ["vbIMEModeAlphaFull"] = 7,
        ["vbIMEModeDisable"] = 3,
        ["vbIMEModeHangul"] = 10,
        ["vbIMEModeHangulFull"] = 9,
        ["vbIMEModeHiragana"] = 4,
        ["vbIMEModeKatakana"] = 5,
        ["vbIMEModeKatakanaHalf"] = 6,
        ["vbIMEModeNoControl"] = 0,
        ["vbIMEModeOff"] = 2,
        ["vbIMEModeOn"] = 1,
        ["vbIMENoOp"] = 0,
        ["vbIMEOff"] = 2,
        ["vbIMEOn"] = 1,
        ["vbIgnore"] = 5,
        ["vbInformation"] = 64,
        ["vbInteger"] = 2,
        ["vbKatakana"] = 16,
        ["vbLong"] = 3,
        ["vbLongDate"] = 1,
        ["vbLongTime"] = 3,
        ["vbLowerCase"] = 2,
        ["vbMaximizedFocus"] = 3,
        ["vbMinimizedFocus"] = 2,
        ["vbMinimizedNoFocus"] = 6,
        ["vbMonday"] = 2,
        ["vbMsgBoxHelpButton"] = 16384,
        ["vbMsgBoxRight"] = 524288,
        ["vbMsgBoxRtlReading"] = 1048576,
        ["vbMsgBoxSetForeground"] = 65536,
        ["vbNarrow"] = 8,
        ["vbNo"] = 7,
        // `vbNormal` is the other name VB6 declares twice — VBA.VbFileAttribute and
        // VBRUN.FormWindowStateConstants — but both are 0, so unlike `vbCancel` there is nothing to
        // choose. The duplicate key is dropped anyway: a repeated key in this initializer is silently
        // order-dependent, and one that is harmless today is a trap the next edit springs.
        ["vbNormalFocus"] = 1,
        ["vbNormalNoFocus"] = 4,
        ["vbNull"] = 1,
        ["vbOK"] = 1,
        ["vbOKCancel"] = 1,
        ["vbOKOnly"] = 0,
        ["vbObject"] = 9,
        ["vbObjectError"] = -2147221504,
        ["vbProperCase"] = 3,
        ["vbQuestion"] = 32,
        ["vbReadOnly"] = 1,
        ["vbRetry"] = 4,
        ["vbRetryCancel"] = 5,
        ["vbSaturday"] = 7,
        ["vbShortDate"] = 2,
        ["vbShortTime"] = 4,
        ["vbSingle"] = 4,
        ["vbString"] = 8,
        ["vbSunday"] = 1,
        ["vbSystem"] = 4,
        ["vbSystemModal"] = 4096,
        ["vbTextCompare"] = 1,
        ["vbThursday"] = 5,
        ["vbTrue"] = -1,
        ["vbTuesday"] = 3,
        ["vbUnicode"] = 64,
        ["vbUpperCase"] = 1,
        ["vbUseDefault"] = -2,
        ["vbUseSystem"] = 0,
        ["vbUseSystemDayOfWeek"] = 0,
        ["vbUserDefinedType"] = 36,
        ["vbVariant"] = 12,
        ["vbVolume"] = 8,
        ["vbWednesday"] = 4,
        ["vbWide"] = 4,
        ["vbYes"] = 6,
        ["vbYesNo"] = 4,
        ["vbYesNoCancel"] = 3,
        ["vbCrLf"] = "\r\n",
        ["vbCr"] = "\r",
        ["vbLf"] = "\n",
    };

    public VB6BuiltIns(IBasicStandardLibrary stdLib)
    {
        this.stdLib = stdLib;
    }

    public async Task<Vb6Value?> EvaluateBuiltInFunction(string name, List<Vb6Value> args)
    {
        // The two async builtins await the standard library; every other builtin is in the sync registry.
        if (string.Equals(name, "msgbox", StringComparison.OrdinalIgnoreCase))
            return await MsgBox(args);
        if (string.Equals(name, "inputbox", StringComparison.OrdinalIgnoreCase))
            return await InputBox(args);
        return Builtins.TryGetValue(name, out var fn) ? fn(this, args, null) : (Vb6Value?)null;
    }

    // ---- Built-in function registry ----
    // Per-group partial files (VB6BuiltIns.Strings.cs, .Math.cs, …) register into this table. It is consulted
    // strictly LAST in name resolution (after local vars/arrays and user procedures), so a user `Function Left()`
    // shadows the intrinsic. The delegate carries `self` (for stateful builtins like Rnd) and the call-site parse
    // context (for error locations); either may be unused.
    internal delegate Vb6Value BuiltinFn(VB6BuiltIns self, IReadOnlyList<Vb6Value> args, ParserRuleContext? ctx);

    private static readonly Dictionary<string, BuiltinFn> Builtins = BuildRegistry();

    private static Dictionary<string, BuiltinFn> BuildRegistry()
    {
        var d = new Dictionary<string, BuiltinFn>(StringComparer.OrdinalIgnoreCase);
        RegisterStrings(d);
        RegisterConversion(d);
        RegisterMath(d);
        RegisterArray(d);
        RegisterInspection(d);
        RegisterDateTime(d);
        RegisterFormat(d);
        // DoEvents — a no-op here (the tree-walking interpreter has no message pump). VB6 yields to the message queue
        // and returns the open-form count; returning Integer 0 lets both `DoEvents` and `x = DoEvents` run without
        // crashing (documented approximation).
        d["DoEvents"] = (_, _, _) => new Vb6Value(0);
        return d;
    }

    // ---- shared coercion helpers (the VB6Visitor.TryUnpack ones aren't reachable here) ----
    private static string AsStr(Vb6Value v) => v.Value?.ToString() ?? "";

    /// <summary>
    /// Was argument <paramref name="i"/> actually supplied at the call site?
    ///
    /// A SKIPPED argument (`Split(s, , 2)`) arrives as <see cref="Vb6Value.Missing"/>, not as Empty — that
    /// is what <c>ExpressionExecutor</c> puts in a blank slot. Testing for Empty instead, as this used to,
    /// meant the default was never selected and `AsInt(Missing)` then threw Err 13 on a perfectly ordinary
    /// call. (#190)
    ///
    /// An EXPLICITLY passed Empty is supplied, and the two are genuinely different in VB6 — measured:
    /// `Split("a b c", , 2)` splits on the default space and gives two elements, while `Dim e : Split("a b
    /// c", e, 2)` uses "" as the delimiter and gives the whole string back. So this tests Missing only.
    ///
    /// Only a MIDDLE argument can be skipped: VB6 rejects a trailing `f(x, )` as a syntax error (measured),
    /// so a short list really does mean "the rest were omitted".
    /// </summary>
    private static bool Supplied(IReadOnlyList<Vb6Value> a, int i) =>
        a.Count > i && a[i].Type != Vb6Value.ValueType.Missing;

    private static int AsInt(Vb6Value v)
    {
        if (v.Value is int i) return i;
        if (v.Value is long l) return (int)l;
        if (v.Value is byte b) return b;
        if (Vb6Value.TryNumericToDouble(v, out var d)) return (int)Math.Round(d, MidpointRounding.ToEven);
        if (v.Type == Vb6Value.ValueType.String) return (int)Math.Round(ToNum(v), MidpointRounding.ToEven);
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    private static double AsDouble(Vb6Value v)
    {
        if (v.Value is bool bo) return bo ? -1 : 0;
        if (Vb6Value.TryNumericToDouble(v, out var d)) return d;
        // A NUMERIC string is a valid operand in VB6 and a non-numeric one is Err 13 — measured: `Abs("5")`
        // is 5 (as a Double), `Abs(" 5 ")` is 5, `Abs("&H10")` is 16, `Abs("5abc")` and `Abs("")` are both
        // Err 13. Rejecting every string, as this used to, was wrong in one direction only, and the defect
        // report that found it described it as the opposite. ToNum is the interpreter's already-pinned
        // string→number rule, so this shares one parser with CDbl and coercion-on-store rather than
        // growing a second one that can drift. (#190)
        if (v.Type == Vb6Value.ValueType.String) return ToNum(v);
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    private async Task<Vb6Value> InputBox(List<Vb6Value> args)
    {
        var prompt = args.Count >= 1 ? args[0].Value?.ToString() : "";
        // InputBox(Prompt, [Title], [Default], ...) — the Title was read, but an omitted one arrived as
        // "" and so could never take the application-name default. Same null-vs-empty rule as MsgBox.
        var caption = args.Count >= 2 ? args[1].Value?.ToString() : null;
        caption ??= AppTitle?.Invoke();
        var defaultText = args.Count >= 3 ? args[2].Value?.ToString() : "";
        var result = await stdLib.InputBox(prompt ?? "", caption, defaultText ?? "");
        return (result ?? "");
    }

    private async Task<Vb6Value> MsgBox(List<Vb6Value> args)
    {
        var text = args.Count >= 1 ? args[0].Value?.ToString() : "";
        var style = (VBMsgBoxStyle)(args.Count >= 2 ? args[1].Value as int? ?? 0 : 0);
        var styleIcon = style & VBMsgBoxStyle.IconBits;
        var styleButtons = style & VBMsgBoxStyle.ButtonsBits;
        var icon = default(MessageBoxIcon);
        var buttons = MessageBoxButtons.Ok;
        if (styleIcon == VBMsgBoxStyle.vbCritical)
            icon = MessageBoxIcon.Error;
        else if (styleIcon == VBMsgBoxStyle.vbExclamation)
            icon = MessageBoxIcon.Warning;
        else if (styleIcon == VBMsgBoxStyle.vbQuestion)
            icon = MessageBoxIcon.Question;
        else if (styleIcon == VBMsgBoxStyle.vbInformation)
            icon = MessageBoxIcon.Information;

        if (styleButtons == VBMsgBoxStyle.vbOKOnly)
            buttons = MessageBoxButtons.Ok;
        else if (styleButtons == VBMsgBoxStyle.vbOKCancel)
            buttons = MessageBoxButtons.OkCancel;
        else if (styleButtons == VBMsgBoxStyle.vbAbortRetryIgnore)
            buttons = MessageBoxButtons.AbortRetryIgnore;
        else if (styleButtons == VBMsgBoxStyle.vbYesNoCancel)
            buttons = MessageBoxButtons.YesNoCancel;
        else if (styleButtons == VBMsgBoxStyle.vbYesNo)
            buttons = MessageBoxButtons.YesNo;
        else if (styleButtons == VBMsgBoxStyle.vbRetryCancel)
            buttons = MessageBoxButtons.RetryCancel;

        // MsgBox(Prompt, [Buttons], [Title], ...). The Title argument used to be dropped on the floor,
        // so every message box came out captionless however it was called.
        //
        // null and "" are NOT the same thing here, which is why this is not `?? ""`: VB6 shows an
        // explicitly empty title as empty, and substitutes the application name only when the argument
        // was OMITTED. Collapsing the two would make `MsgBox "x", 0, ""` sprout a caption the author
        // deliberately suppressed. Supplying the omitted-case default is the caller's job — and properly
        // it is App.Title, which does not exist yet (#136).
        var title = args.Count >= 3 ? args[2].Value?.ToString() : null;
        // Omitted (null) takes App.Title, as in VB6. An explicitly empty title is NOT omitted and stays
        // empty — see #131 — so this substitutes only for null.
        title ??= AppTitle?.Invoke();

        var result = await stdLib.MsgBox(text ?? "", title, buttons, icon);
        var vbResult = result switch
        {
            MessageBoxResult.None => VBMsgBoxResult.vbOK,
            MessageBoxResult.Ok => VBMsgBoxResult.vbOK,
            MessageBoxResult.Cancel => VBMsgBoxResult.vbCancel,
            MessageBoxResult.Abort => VBMsgBoxResult.vbAbort,
            MessageBoxResult.Retry => VBMsgBoxResult.vbRetry,
            MessageBoxResult.Ignore => VBMsgBoxResult.vbIgnore,
            MessageBoxResult.Yes => VBMsgBoxResult.vbYes,
            MessageBoxResult.No => VBMsgBoxResult.vbNo,
            MessageBoxResult.TryAgain => VBMsgBoxResult.vbOK,
            MessageBoxResult.Continue => VBMsgBoxResult.vbOK,
            _ => throw new ArgumentOutOfRangeException()
        };
        return (int)vbResult;
    }

    // Mid / UCase / LCase moved to VB6BuiltIns.Strings.cs; LBound / UBound to VB6BuiltIns.Array.cs.

    public bool TryGetBuiltInConstant(string name, out Vb6Value constant)
    {
        return builtInConstants.TryGetValue(name, out constant);
    }
}