/*
 * Copyright (C) 2017, Ulrich Wolffgang <ulrich.wolffgang@proleap.io> All rights reserved.
 *
 * This software may be modified and distributed under the terms of the MIT license. See the LICENSE
 * file for details.
 */

/*
 * Visual Basic 6.0 Grammar for ANTLR4
 *
 * This is a Visual Basic 6.0 grammar, which is part of the Visual Basic 6.0 parser at
 * https://github.com/uwol/vb6parser.
 *
 * The grammar is derived from the Visual Basic 6.0 language reference
 * http://msdn.microsoft.com/en-us/library/aa338033%28v=vs.60%29.aspx and has been tested with MSDN
 * VB6 statements as well as several Visual Basic 6.0 code repositories.
 */

// $antlr-format alignTrailingComments true, columnLimit 150, minEmptyLines 1, maxEmptyLinesToKeep 1, reflowComments false, useTab false
// $antlr-format allowShortRulesOnASingleLine false, allowShortBlocksOnASingleLine true, alignSemicolons hanging, alignColons hanging

parser grammar VisualBasic6Parser;

options {
    tokenVocab = VisualBasic6Lexer;
}

// module ----------------------------------

startRule
    : module EOF
    ;

module
    : WS? NEWLINE* (moduleHeader NEWLINE+)? moduleReferences? NEWLINE* controlProperties? NEWLINE* moduleConfig? NEWLINE* moduleAttributes? NEWLINE*
        moduleOptions? NEWLINE* moduleBody? NEWLINE* WS?
    ;

moduleReferences
    : moduleReference+
    ;

moduleReference
    : OBJECT WS? EQ WS? moduleReferenceValue (SEMICOLON WS? moduleReferenceComponent)? NEWLINE*
    ;

moduleReferenceValue
    : STRINGLITERAL
    ;

moduleReferenceComponent
    : STRINGLITERAL
    ;

moduleHeader
    : VERSION WS doubleLiteral (WS CLASS)?
    ;

moduleConfig
    : BEGIN NEWLINE+ moduleConfigElement+ END NEWLINE+
    ;

moduleConfigElement
    : ambiguousIdentifier WS? EQ WS? literal NEWLINE
    ;

moduleAttributes
    : (attributeStmt blockSep)+
    ;

moduleOptions
    : (moduleOption blockSep)+
    ;

moduleOption
    : OPTION_BASE WS integerLiteral     # optionBaseStmt
    | OPTION_COMPARE WS (BINARY | TEXT) # optionCompareStmt
    | OPTION_EXPLICIT                   # optionExplicitStmt
    | OPTION_PRIVATE_MODULE             # optionPrivateModuleStmt
    ;

moduleBody
    : moduleBodyElement (blockSep moduleBodyElement)*
    ;

moduleBodyElement
    : moduleBlock
    | moduleOption
    | declareStmt
    | enumerationStmt
    | eventStmt
    | functionStmt
    | macroIfThenElseStmt
    | propertyGetStmt
    | propertySetStmt
    | propertyLetStmt
    | subStmt
    | typeStmt
    ;

// controls ----------------------------------

controlProperties
    : WS? BEGIN WS cp_ControlType WS cp_ControlIdentifier WS? NEWLINE+ cp_Properties+ END NEWLINE*
    ;

cp_Properties
    : cp_SingleProperty
    | cp_NestedProperty
    | controlProperties
    ;

cp_SingleProperty
    : WS? (DESIGNER_KEY | implicitCallStmt_InStmt) WS? EQ WS? '$'? cp_PropertyValue FRX_OFFSET? NEWLINE+
    ;

cp_PropertyName
    : (OBJECT DOT)? ambiguousIdentifier (LPAREN literal RPAREN)? (
        DOT ambiguousIdentifier (LPAREN literal RPAREN)?
    )*
    ;

cp_PropertyValue
    : DOLLAR? (literal | (LBRACE ambiguousIdentifier RBRACE) | POW ambiguousIdentifier)
    ;

cp_NestedProperty
    : WS? BEGINPROPERTY WS ambiguousIdentifier (LPAREN integerLiteral RPAREN)? (WS GUID)? NEWLINE+ (
        cp_Properties+
    )? ENDPROPERTY NEWLINE+
    ;

cp_ControlType
    : complexType
    ;

cp_ControlIdentifier
    : ambiguousIdentifier
    ;

// block ----------------------------------

moduleBlock
    : block
    ;

attributeStmt
    : ATTRIBUTE WS implicitCallStmt_InStmt WS? EQ WS? literal (WS? COMMA WS? literal)*
    ;

block
    : emptyLineNumber* lineHead? blockStmt
      ( colonSep blockStmt
      | lineSep emptyLineNumber* lineHead? blockStmt
      )*
      blockSep?
    ;

// What may stand at the head of a LINE, before the first statement on it. Reachable only after a
// `lineSep` - a separator containing a real line break - and never after a `colonSep`, because VB6 allows
// a label only at a line head: `Skip: x = 1` is legal and `z = 1: Here: z = 2` is READ AS A CALL
// ("Sub or Function not defined"). A head reachable after a colon would invent a jump target the program
// does not have. Order is measured: `10 Skip: stmt` is legal, `Skip: 10 stmt` is a syntax error.
// Mirrored from the interpreter's grammar.
lineHead
    : lineNumber WS? COLON? WS? (lineLabel WS?)?
    | lineLabel WS?
    ;

// A separator made only of colons: still the same line.
colonSep
    : (WS? COLON WS?)+
    ;

// A separator containing at least one real line break. The only place a lineHead may follow.
lineSep
    : (WS? COLON WS?)* WS? NEWLINE WS? (WS? (NEWLINE | COLON) WS?)*
    ;

// A line number with NO statement after it on the line. `10` alone is a legal jump target, and so is
// `10 Rem a remark` once the remark is a comment - both leave a number with an empty line behind it.
// Without this the number is an "extraneous input" and the whole procedure fails to parse. It labels the
// NEXT executable statement. Mirrored from the interpreter's grammar.
emptyLineNumber
    : lineNumber WS? COLON? WS? blockSep
    ;

// A statement separator: a newline, a colon, or a run of them. Mirrored from the interpreter's
// grammar, where the conformance corpus showed the old lexer rule (a colon counted only when a
// SPACE followed it) rejected `a = 1:b = 2`, `a::b`, a trailing colon and a colon before Else — all
// ordinary VB6. It cannot live in NEWLINE: widening that to swallow a bare colon deletes the token
// lineLabel needs to be a label at all, so separating statements belongs to the parser.
blockSep
    : (WS? (NEWLINE | COLON) WS?)+
    ;

blockStmt
    : appActivateStmt
    | attributeStmt
    | beepStmt
    | chDirStmt
    | chDriveStmt
    | closeStmt
    | constStmt
    | dateStmt
    | deleteSettingStmt
    | deftypeStmt
    | doLoopStmt
    | endStmt
    | eraseStmt
    | errorStmt
    | exitStmt
    | explicitCallStmt
    | filecopyStmt
    | forEachStmt
    | forNextStmt
    | getStmt
    | goSubStmt
    | goToStmt
    | ifThenElseStmt
    | implementsStmt
    | inputStmt
    | killStmt
    | letStmt
    | lineInputStmt
    | lineLabel
    | loadStmt
    | lockStmt
    | lsetStmt
    | macroIfThenElseStmt
    | midStmt
    | mkdirStmt
    | nameStmt
    | onErrorStmt
    | onGoToStmt
    | onGoSubStmt
    | openStmt
    | printStmt
    | putStmt
    | raiseEventStmt
    | randomizeStmt
    | redimStmt
    | resetStmt
    | resumeStmt
    | returnStmt
    | rmdirStmt
    | rsetStmt
    | savepictureStmt
    | saveSettingStmt
    | seekStmt
    | selectCaseStmt
    | sendkeysStmt
    | setattrStmt
    | setStmt
    | stopStmt
    | timeStmt
    | unloadStmt
    | unlockStmt
    | variableStmt
    | whileWendStmt
    | widthStmt
    | withStmt
    | writeStmt
    | implicitCallStmt_InBlock
    ;

// statements ----------------------------------

appActivateStmt
    : APPACTIVATE WS valueStmt (WS? COMMA WS? valueStmt)?
    ;

beepStmt
    : BEEP
    ;

chDirStmt
    : CHDIR WS valueStmt
    ;

chDriveStmt
    : CHDRIVE WS valueStmt
    ;

closeStmt
    : CLOSE (WS valueStmt (WS? COMMA WS? valueStmt)*)?
    ;

constStmt
    : (publicPrivateGlobalVisibility WS)? CONST WS constSubStmt (WS? COMMA WS? constSubStmt)*
    ;

constSubStmt
    : ambiguousIdentifier typeHint? (WS asTypeClause)? WS? EQ WS? valueStmt
    ;

dateStmt
    : DATE WS? EQ WS? valueStmt
    ;

// `Lib WS? "x"` / `Alias WS? "x"` - measured, not reasoned; see the note on selectCaseStmt.
declareStmt
    : (visibility WS)? DECLARE WS (FUNCTION typeHint? | SUB) WS ambiguousIdentifier typeHint? WS LIB WS? STRINGLITERAL (
        WS ALIAS WS? STRINGLITERAL
    )? (WS? argList)? (WS asTypeClause)?
    ;

deftypeStmt
    : (
        DEFBOOL
        | DEFBYTE
        | DEFINT
        | DEFLNG
        | DEFCUR
        | DEFSNG
        | DEFDBL
        | DEFDEC
        | DEFDATE
        | DEFSTR
        | DEFOBJ
        | DEFVAR
    ) WS letterrange (WS? COMMA WS? letterrange)*
    ;

deleteSettingStmt
    : DELETESETTING WS valueStmt WS? COMMA WS? valueStmt (WS? COMMA WS? valueStmt)?
    ;

doLoopStmt
    : DO blockSep (block blockSep)? LOOP
    | DO WS (WHILE | UNTIL) WS valueStmt blockSep ( block blockSep)? LOOP
    | DO blockSep (block blockSep)? LOOP WS (WHILE | UNTIL) WS valueStmt
    ;

endStmt
    : END
    ;

enumerationStmt
    : (publicPrivateVisibility WS)? ENUM WS ambiguousIdentifier blockSep (enumerationStmt_Constant)* END_ENUM
    ;

enumerationStmt_Constant
    : ambiguousIdentifier (WS? EQ WS? valueStmt)? blockSep
    ;

eraseStmt
    : ERASE WS valueStmt (WS? COMMA WS? valueStmt)*
    ;

errorStmt
    : ERROR WS valueStmt
    ;

eventStmt
    : (visibility WS)? EVENT WS ambiguousIdentifier WS? argList
    ;

exitStmt
    : EXIT_DO
    | EXIT_FOR
    | EXIT_FUNCTION
    | EXIT_PROPERTY
    | EXIT_SUB
    ;

filecopyStmt
    : FILECOPY WS valueStmt WS? COMMA WS? valueStmt
    ;

// `For _ Each` - WS? between the two keywords; see the note on selectCaseStmt.
forEachStmt
    : FOR WS? EACH WS ambiguousIdentifier typeHint? WS IN WS valueStmt blockSep (block blockSep)? NEXT (
        WS ambiguousIdentifier
    )?
    ;

forNextStmt
    : FOR WS iCS_S_VariableOrProcedureCall typeHint? (WS asTypeClause)? WS? EQ WS? valueStmt WS TO WS valueStmt (
        WS STEP WS valueStmt
    )? blockSep (block blockSep)? NEXT (WS ambiguousIdentifier typeHint?)?
    ;

functionStmt
    : (visibility WS)? (STATIC WS)? FUNCTION WS ambiguousIdentifier typeHint? (WS? argList)? (
        WS asTypeClause
    )? blockSep (block blockSep)? END_FUNCTION
    ;

getStmt
    : GET WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt
    ;

goSubStmt
    : GOSUB WS valueStmt
    ;

goToStmt
    : GOTO WS valueStmt
    ;

// One branch of a SINGLE-LINE If: a colon-joined run of statements, all belonging to the branch.
// Mirrored from the interpreter's grammar. Measured against vb6.exe, because the naive reading is wrong
// in the dangerous direction: in `If False Then A : B` VB6 runs NEITHER statement - the whole joined
// tail is the Then branch. A colon may also sit immediately before Else, so the statement after a colon
// is optional. Newlines are deliberately not accepted; that is what separates this from the block form.
inlineIfBody
    : (WS? COLON WS?)* blockStmt (WS? COLON (WS? blockStmt)?)*
    ;

ifThenElseStmt
    // Clean-room: single-line If permits an empty Then (Then Else ...), a bare line number as an
    // implicit-GoTo target, and a single/nested body (VB6 Language Reference, If...Then...Else).
    // The else body is OPTIONAL: `If c Then x = 1 Else Rem nothing` is legal while `If c Then Rem
    // nothing` is a syntax error, and a comment is invisible to the parser either way - so the only way
    // to accept the first is to allow an empty else. A bare trailing `Else` is legal too, measured, which
    // is what makes this faithful rather than a widening. Mirrored from the interpreter's grammar.
    // `WS?` after THEN: `If True Then: Debug.Print "A"` is legal and was refused, because the mandatory
    // whitespace had nothing to match - inlineIfBody's own leading `(WS? COLON WS?)*` already admits the
    // colon. Safe by adjacency: `ThenX` lexes as one IDENTIFIER. Mirrored from the interpreter's grammar.
    : IF WS ifConditionStmt WS THEN WS? ((inlineIfBody | lineNumber) (WS? ELSE (WS (inlineIfBody | lineNumber))?)? | ELSE (WS (inlineIfBody | lineNumber))?) # inlineIfThenElse
    | ifBlockStmt ifElseIfBlockStmt* ifElseBlockStmt? END_IF             # blockIfThenElse
    ;

ifBlockStmt
    : IF WS ifConditionStmt WS THEN blockSep (block blockSep)?
    ;

ifConditionStmt
    : valueStmt
    ;

ifElseIfBlockStmt
    : ELSEIF WS ifConditionStmt WS THEN (WS | NEWLINE)+ (block blockSep)?
    ;

ifElseBlockStmt
    : ELSE blockSep (block blockSep)?
    ;

implementsStmt
    : IMPLEMENTS WS ambiguousIdentifier
    ;

inputStmt
    : INPUT WS valueStmt (WS? COMMA WS? valueStmt)+
    ;

killStmt
    : KILL WS valueStmt
    ;

letStmt
    : (LET WS)? implicitCallStmt_InStmt WS? (EQ | PLUS_EQ | MINUS_EQ) WS? valueStmt
    ;

lineInputStmt
    : LINE_INPUT WS valueStmt WS? COMMA WS? valueStmt
    ;

loadStmt
    : LOAD WS valueStmt
    ;

lockStmt
    : LOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?
    ;

lsetStmt
    : LSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt
    ;

macroIfThenElseStmt
    : macroIfBlockStmt macroElseIfBlockStmt* macroElseBlockStmt? MACRO_END_IF
    ;

macroIfBlockStmt
    : MACRO_IF WS ifConditionStmt WS THEN NEWLINE+ (moduleBody NEWLINE+)?
    ;

macroElseIfBlockStmt
    : MACRO_ELSEIF WS ifConditionStmt WS THEN NEWLINE+ (moduleBody NEWLINE+)?
    ;

macroElseBlockStmt
    : MACRO_ELSE NEWLINE+ (moduleBody NEWLINE+)?
    ;

midStmt
    : MID WS? LPAREN WS? argsCall WS? RPAREN
    ;

mkdirStmt
    : MKDIR WS valueStmt
    ;

nameStmt
    : NAME WS valueStmt WS AS WS valueStmt
    ;

// `Resume _ Next` - WS? between the two keywords; see the note on selectCaseStmt.
onErrorStmt
    : (ON_ERROR | ON_LOCAL_ERROR) WS (GOTO WS valueStmt COLON? | RESUME WS? NEXT)
    ;

onGoToStmt
    : ON WS valueStmt WS GOTO WS valueStmt (WS? COMMA WS? valueStmt)*
    ;

onGoSubStmt
    : ON WS valueStmt WS GOSUB WS valueStmt (WS? COMMA WS? valueStmt)*
    ;

openStmt
    : OPEN WS valueStmt WS FOR WS (APPEND | BINARY | INPUT | OUTPUT | RANDOM) (
        WS ACCESS WS (READ | WRITE | READ_WRITE)
    )? (WS (SHARED | LOCK_READ | LOCK_WRITE | LOCK_READ_WRITE))? WS AS WS valueStmt (
        WS LEN WS? EQ WS? valueStmt
    )?
    ;

outputList
    : outputList_Expression (WS? (SEMICOLON | COMMA) WS? outputList_Expression?)*
    | outputList_Expression? (WS? (SEMICOLON | COMMA) WS? outputList_Expression?)+
    ;

outputList_Expression
    : (SPC | TAB) (WS? LPAREN WS? argsCall WS? RPAREN)?
    | valueStmt
    ;

printStmt
    : PRINT WS valueStmt WS? COMMA (WS? outputList)?
    ;

propertyGetStmt
    : (visibility WS)? (STATIC WS)? PROPERTY_GET WS ambiguousIdentifier typeHint? (WS? argList)? (
        WS asTypeClause
    )? blockSep (block blockSep)? END_PROPERTY
    ;

propertySetStmt
    : (visibility WS)? (STATIC WS)? PROPERTY_SET WS ambiguousIdentifier (WS? argList)? blockSep (
        block blockSep
    )? END_PROPERTY
    ;

propertyLetStmt
    : (visibility WS)? (STATIC WS)? PROPERTY_LET WS ambiguousIdentifier (WS? argList)? blockSep (
        block blockSep
    )? END_PROPERTY
    ;

putStmt
    : PUT WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt
    ;

raiseEventStmt
    : RAISEEVENT WS ambiguousIdentifier (WS? LPAREN WS? (argsCall WS?)? RPAREN)?
    ;

randomizeStmt
    : RANDOMIZE (WS valueStmt)?
    ;

redimStmt
    : REDIM WS (PRESERVE WS)? redimSubStmt (WS? COMMA WS? redimSubStmt)*
    ;

redimSubStmt
    : implicitCallStmt_InStmt WS? LPAREN WS? subscripts WS? RPAREN (WS asTypeClause)?
    ;

resetStmt
    : RESET
    ;

resumeStmt
    : RESUME (WS (NEXT | ambiguousIdentifier))?
    ;

returnStmt
    : RETURN
    ;

rmdirStmt
    : RMDIR WS valueStmt
    ;

rsetStmt
    : RSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt
    ;

savepictureStmt
    : SAVEPICTURE WS valueStmt WS? COMMA WS? valueStmt
    ;

saveSettingStmt
    : SAVESETTING WS valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt
    ;

seekStmt
    : SEEK WS valueStmt WS? COMMA WS? valueStmt
    ;

// `Select _` / `Case` is legal VB6, and the WS between the two keywords was mandatory, so it was not.
// The other multi-word keywords are single lexer tokens and were fixed there with KWSEP; these three
// (`Select Case`, `For Each`, `Resume Next`) are assembled by the PARSER instead, and a continuation is
// invisible to it - the lexer has already skipped it. So the WS has to become optional.
//
// Optional is not a widening here. Two adjacent word-tokens cannot occur in the stream without something
// between them: `SelectCase` lexes as one IDENTIFIER, never as SELECT then CASE. So the only way the
// parser ever sees them adjacent is that a skipped token - a continuation - sat between them, which is
// exactly the case being admitted. Applied ONLY where both neighbours are word-tokens; the
// That argument does NOT reach the keyword-then-literal pairs (`Lib "x"`, `Alias "x"`), because a keyword
// and a string CAN abut with nothing between them. Those were measured instead, and the answer was better
// than the argument: vb6.exe compiles `Lib"kernel32"` with no separator at all, so WS? there is not a
// widening either - it is what VB6 does. Mirrored from the interpreter's grammar.
selectCaseStmt
    : SELECT WS? CASE WS valueStmt blockSep sC_Case* WS? END_SELECT
    ;

sC_Case
    : CASE WS sC_Cond WS? (COLON? NEWLINE* | blockSep) (block blockSep)?
    ;

// ELSE first, so that it is not interpreted as a variable call
sC_Cond
    : ELSE                                     # caseCondElse
    | sC_CondExpr (WS? COMMA WS? sC_CondExpr)* # caseCondExpr
    ;

sC_CondExpr
    : IS WS? comparisonOperator WS? valueStmt # caseCondExprIs
    | valueStmt                               # caseCondExprValue
    | valueStmt WS TO WS valueStmt            # caseCondExprTo
    ;

sendkeysStmt
    : SENDKEYS WS valueStmt (WS? COMMA WS? valueStmt)?
    ;

setattrStmt
    : SETATTR WS valueStmt WS? COMMA WS? valueStmt
    ;

setStmt
    : SET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt
    ;

stopStmt
    : STOP
    ;

subStmt
    : (visibility WS)? (STATIC WS)? SUB WS ambiguousIdentifier (WS? argList)? blockSep (
        block blockSep
    )? END_SUB
    ;

timeStmt
    : TIME WS? EQ WS? valueStmt
    ;

typeStmt
    : (visibility WS)? TYPE WS ambiguousIdentifier blockSep (typeStmt_Element)* END_TYPE
    ;

typeStmt_Element
    : ambiguousIdentifier (WS? LPAREN (WS? subscripts)? WS? RPAREN)? (WS asTypeClause)? blockSep
    ;

typeOfStmt
    : TYPEOF WS valueStmt (WS IS WS type_)?
    ;

unloadStmt
    : UNLOAD WS valueStmt
    ;

unlockStmt
    : UNLOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?
    ;

// operator precedence is represented by rule order
valueStmt
    : literal                                                                  # vsLiteral
    | LPAREN WS? valueStmt (WS? COMMA WS? valueStmt)* WS? RPAREN               # vsStruct
    | NEW WS valueStmt                                                         # vsNew
    | typeOfStmt                                                               # vsTypeOf
    | ADDRESSOF WS valueStmt                                                   # vsAddressOf
    | implicitCallStmt_InStmt WS? ASSIGN WS? valueStmt                         # vsAssign
    | valueStmt WS? POW WS? valueStmt                                          # vsPow
    | (PLUS | MINUS) WS? valueStmt                                             # vsPlusMinus
    | valueStmt WS? (MULT | DIV) WS? valueStmt                                 # vsMultDiv
    | valueStmt WS? (IDIV) WS? valueStmt                                       # vsIDiv
    | valueStmt WS? MOD WS? valueStmt                                          # vsMod
    | valueStmt WS? (PLUS | MINUS) WS? valueStmt                               # vsAddSub
    | valueStmt WS? AMPERSAND WS? valueStmt                                    # vsAmp
    | valueStmt WS? (EQ | NEQ | LT | GT | LEQ | GEQ | LIKE | IS) WS? valueStmt # vsComp
    | NOT (WS valueStmt | LPAREN WS? valueStmt WS? RPAREN)                     # vsNot
    | valueStmt WS? AND WS? valueStmt                                          # vsAnd
    | valueStmt WS? OR WS? valueStmt                                           # vsOr
    | valueStmt WS? XOR WS? valueStmt                                          # vsXor
    | valueStmt WS? EQV WS? valueStmt                                          # vsEqv
    | valueStmt WS? IMP WS? valueStmt                                          # vsImp
    | implicitCallStmt_InStmt                                                  # vsICS
    | midStmt                                                                  # vsMid
    ;

variableStmt
    : (DIM | STATIC | visibility) WS (WITHEVENTS WS)? variableListStmt
    ;

variableListStmt
    : variableSubStmt (WS? COMMA WS? variableSubStmt)*
    ;

variableSubStmt
    : ambiguousIdentifier typeHint? (WS? LPAREN WS? (subscripts WS?)? RPAREN WS?)? (
        WS asTypeClause
    )?
    ;

whileWendStmt
    : WHILE WS valueStmt blockSep block* NEWLINE* WEND
    ;

widthStmt
    : WIDTH WS valueStmt WS? COMMA WS? valueStmt
    ;

withStmt
    : WITH WS (NEW WS)? implicitCallStmt_InStmt blockSep (block blockSep)? END_WITH
    ;

writeStmt
    : WRITE WS valueStmt WS? COMMA (WS? outputList)?
    ;

// complex call statements ----------------------------------

explicitCallStmt
    : eCS_ProcedureCall
    | eCS_MemberProcedureCall
    ;

// parantheses are required in case of args -> empty parantheses are removed
eCS_ProcedureCall
    : CALL WS ambiguousIdentifier typeHint? (WS? LPAREN WS? argsCall WS? RPAREN)?
    ;

// parantheses are required in case of args -> empty parantheses are removed
eCS_MemberProcedureCall
    : CALL WS implicitCallStmt_InStmt? DOT WS? ambiguousIdentifier typeHint? (
        WS? LPAREN WS? argsCall WS? RPAREN
    )?
    ;

implicitCallStmt_InBlock
    : iCS_B_ProcedureCall
    | iCS_B_MemberProcedureCall
    ;

// parantheses are forbidden in case of args variables cannot be called in blocks certainIdentifier
// instead of ambiguousIdentifier for preventing ambiguity with statement keywords
iCS_B_ProcedureCall
    : certainIdentifier (WS argsCall)?
    ;

iCS_B_MemberProcedureCall
    : implicitCallStmt_InStmt? DOT ambiguousIdentifier typeHint? (WS argsCall)? dictionaryCallStmt?
    ;

// iCS_S_MembersCall first, so that member calls are not resolved as separate iCS_S_VariableOrProcedureCalls
implicitCallStmt_InStmt
    : iCS_S_MembersCall
    | iCS_S_VariableOrProcedureCall
    | iCS_S_ProcedureOrArrayCall
    | iCS_S_DictionaryCall
    ;

iCS_S_VariableOrProcedureCall
    : ambiguousIdentifier typeHint? dictionaryCallStmt?
    ;

iCS_S_ProcedureOrArrayCall
    : (ambiguousIdentifier | baseType | iCS_S_NestedProcedureCall) typeHint? WS? (
        LPAREN WS? (argsCall WS?)? RPAREN
    )+ dictionaryCallStmt?
    ;

iCS_S_NestedProcedureCall
    : ambiguousIdentifier typeHint? WS? LPAREN WS? (argsCall WS?)? RPAREN
    ;

iCS_S_MembersCall
    : (iCS_S_VariableOrProcedureCall | iCS_S_ProcedureOrArrayCall)? iCS_S_MemberCall+ dictionaryCallStmt?
    ;

iCS_S_MemberCall
    : WS? DOT (iCS_S_VariableOrProcedureCall | iCS_S_ProcedureOrArrayCall)
    ;

iCS_S_DictionaryCall
    : dictionaryCallStmt
    ;

// atomic call statements ----------------------------------

argsCall
    : (argCall? WS? (COMMA | SEMICOLON) WS?)* argCall (WS? (COMMA | SEMICOLON) WS? argCall?)*
    ;

argCall
    : ((BYVAL | BYREF | PARAMARRAY) WS)? valueStmt
    ;

dictionaryCallStmt
    : EXCLAMATIONMARK ambiguousIdentifier typeHint?
    ;

// atomic rules for statements
argList
    : LPAREN (WS? arg (WS? COMMA WS? arg)*)? WS? RPAREN
    ;

arg
    : (OPTIONAL WS)? ((BYVAL | BYREF) WS)? (PARAMARRAY WS)? ambiguousIdentifier typeHint? (
        WS? LPAREN WS? RPAREN
    )? (WS asTypeClause)? (WS? argDefaultValue)?
    ;

argDefaultValue
    : EQ WS? valueStmt
    ;

subscripts
    : subscript_ (WS? COMMA WS? subscript_)*
    ;

subscript_
    : (valueStmt WS TO WS)? valueStmt
    ;

// atomic rules ----------------------------------

ambiguousIdentifier
    : (IDENTIFIER | ambiguousKeyword)+
    | L_SQUARE_BRACKET (IDENTIFIER | ambiguousKeyword)+ R_SQUARE_BRACKET
    ;

asTypeClause
    : AS WS (NEW WS)? type_ (WS fieldLength)?
    ;

baseType
    : BOOLEAN
    | BYTE
    | COLLECTION
    | DATE
    | DOUBLE
    | INTEGER
    | LONG
    | OBJECT
    | SINGLE
    | STRING
    | VARIANT
    ;

certainIdentifier
    : IDENTIFIER (ambiguousKeyword | IDENTIFIER)*
    | ambiguousKeyword (ambiguousKeyword | IDENTIFIER)+
    ;

comparisonOperator
    : LT
    | LEQ
    | GT
    | GEQ
    | EQ
    | NEQ
    | IS
    | LIKE
    ;

complexType
    : ambiguousIdentifier (DOT ambiguousIdentifier)*
    ;

fieldLength
    : MULT WS? (integerLiteral | ambiguousIdentifier)
    ;

letterrange
    : certainIdentifier (WS? MINUS WS? certainIdentifier)?
    ;

// `Name:` at a line head. The whitespace before the colon is a RUN: `Later : stmt` and `Skip<tab>: stmt`
// are both legal VB6, and demanding adjacency meant those lines parsed as a bare call plus a separator,
// losing the label silently. Trailing colons are absorbed (`Skip:: stmt` is legal). Reachable both as a
// lineHead (sharing its line with a statement) and as a blockStmt (standing alone on its line); the two
// never compete. Mirrored from the interpreter's grammar.
lineLabel
    : labelName WS? COLON (WS? COLON)*
    ;

// WHICH NAMES MAY BE A LABEL - measured one probe per word, and not derivable from anything. `Reset:` is
// a label; `Randomize:` is the Randomize statement. `Beep:` is a label; `Stop:` is a syntax error. No
// structural property separates those pairs. It must be narrow: a label rule taking any
// ambiguousIdentifier eats the `Else:` of a block If (ELSE is an ambiguousKeyword) and swallows the whole
// else-branch into the Then-block - a wrong value, silently. Mirrored from the interpreter's grammar.
labelName
    : IDENTIFIER
    | BEEP
    | ERROR
    | KILL
    | LOAD
    | MID
    | NAME
    | RESET
    | TIME
    | WIDTH
    ;

literal
    : COLORLITERAL
    | DATELITERAL
    | doubleLiteral
    | FILENUMBER
    | integerLiteral
    | octalLiteral
    | STRINGLITERAL
    | TRUE
    | FALSE
    | NOTHING
    | NULL_
    | EMPTY_
    ;

publicPrivateVisibility
    : PRIVATE
    | PUBLIC
    ;

publicPrivateGlobalVisibility
    : PRIVATE
    | PUBLIC
    | GLOBAL
    ;

type_
    : (baseType | complexType) (WS? LPAREN WS? RPAREN)?
    ;

typeHint
    : AMPERSAND
    | AT
    | DOLLAR
    | EXCLAMATIONMARK
    | HASH
    | PERCENT
    ;

visibility
    : PRIVATE
    | PUBLIC
    | FRIEND
    | GLOBAL
    ;

// ambiguous keywords
ambiguousKeyword
    : ACCESS
    | ADDRESSOF
    | ALIAS
    | AND
    | ATTRIBUTE
    | APPACTIVATE
    | APPEND
    | AS
    | BEEP
    | BEGIN
    | BINARY
    | BOOLEAN
    | BYVAL
    | BYREF
    | BYTE
    | CALL
    | CASE
    | CLASS
    | CLOSE
    | CHDIR
    | CHDRIVE
    | COLLECTION
    | CONST
    | DATE
    | DECLARE
    | DEFBOOL
    | DEFBYTE
    | DEFCUR
    | DEFDBL
    | DEFDATE
    | DEFDEC
    | DEFINT
    | DEFLNG
    | DEFOBJ
    | DEFSNG
    | DEFSTR
    | DEFVAR
    | DELETESETTING
    | DIM
    | DO
    | DOUBLE
    | EACH
    | ELSE
    | ELSEIF
    | END
    | ENUM
    | EMPTY_
    | EQV
    | ERASE
    | NAME
    | ERROR
    | EVENT
    | FALSE
    | FILECOPY
    | FRIEND
    | FOR
    | FUNCTION
    | GET
    | GLOBAL
    | GOSUB
    | GOTO
    | IF
    | IMP
    | IMPLEMENTS
    | IN
    | INPUT
    | IS
    | INTEGER
    | KILL
    | LOAD
    | LOCK
    | LONG
    | LOOP
    | LEN
    | LET
    | LIB
    | LIKE
    | LSET
    | ME
    | MID
    | MKDIR
    | MOD
    | NAME
    | NEXT
    | NEW
    | NOT
    | NOTHING
    | NULL_
    | OBJECT
    | ON
    | OPEN
    | OPTIONAL
    | OR
    | OUTPUT
    | PARAMARRAY
    | PRESERVE
    | PRINT
    | PRIVATE
    | PUBLIC
    | PUT
    | RANDOM
    | RANDOMIZE
    | RAISEEVENT
    | READ
    | REDIM
    | REM
    | RESET
    | RESUME
    | RETURN
    | RMDIR
    | RSET
    | SAVEPICTURE
    | SAVESETTING
    | SEEK
    | SELECT
    | SENDKEYS
    | SET
    | SETATTR
    | SHARED
    | SINGLE
    | SPC
    | STATIC
    | STEP
    | STOP
    | STRING
    | SUB
    | TAB
    | TEXT
    | THEN
    | TIME
    | TO
    | TRUE
    | TYPE
    | TYPEOF
    | UNLOAD
    | UNLOCK
    | UNTIL
    | VARIANT
    | VERSION
    | WEND
    | WHILE
    | WIDTH
    | WITH
    | WITHEVENTS
    | WRITE
    | XOR
    ;

integerLiteral
    : (PLUS | MINUS)* INTEGERLITERAL
    ;

octalLiteral
    : (PLUS | MINUS)* OCTALLITERAL
    ;

doubleLiteral
    : (PLUS | MINUS)* DOUBLELITERAL
    ;
// VB6 numeric line number: an unsigned integer at the start of a line, usable as a label /
// branch target (VB6 Language Reference: Line numbers, 0-2147483647). Clean-room: authored from
// the language reference + the local corpus; not derived from any other grammar.
lineNumber
    : INTEGERLITERAL
    ;
