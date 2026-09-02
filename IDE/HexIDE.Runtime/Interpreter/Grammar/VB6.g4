/*
* SOURCE: https://github.com/uwol/proleap-vb6-parser/blob/main/src/main/antlr4/io/proleap/vb6/VisualBasic6.g4
* Copyright (C) 2017, Ulrich Wolffgang <ulrich.wolffgang@proleap.io>
* All rights reserved.
*
* This software may be modified and distributed under the terms
* of the MIT license. See the LICENSE file for details.
*/

/*
* Visual Basic 6.0 Grammar for ANTLR4
*
* This is a Visual Basic 6.0 grammar, which is part of the Visual Basic 6.0
* parser at https://github.com/uwol/proleap-vb6-parser.
*
* The grammar is derived from the Visual Basic 6.0 language reference
* http://msdn.microsoft.com/en-us/library/aa338033%28v=vs.60%29.aspx
* and has been tested with MSDN VB6 statements as well as several Visual
* Basic 6.0 code repositories.
*/

grammar VB6;

// module ----------------------------------

startRule
   : module EOF
   ;

module
   : WS? NEWLINE* (moduleHeader NEWLINE +)? moduleReferences? NEWLINE* controlProperties? NEWLINE* moduleConfig? NEWLINE* moduleAttributes? NEWLINE* moduleOptions? NEWLINE* moduleBody? NEWLINE* WS?
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
   : VERSION WS DOUBLELITERAL (WS CLASS)?
   ;

moduleConfig
   : BEGIN NEWLINE + moduleConfigElement + END NEWLINE +
   ;

moduleConfigElement
   : ambiguousIdentifier WS? EQ WS? literal NEWLINE
   ;

moduleAttributes
   : (attributeStmt blockSep) +
   ;

moduleOptions
   : (moduleOption blockSep) +
   ;

moduleOption
   : OPTION_BASE WS INTEGERLITERAL # optionBaseStmt
   | OPTION_COMPARE WS (BINARY | TEXT) # optionCompareStmt
   | OPTION_EXPLICIT # optionExplicitStmt
   | OPTION_PRIVATE_MODULE # optionPrivateModuleStmt
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
	| controlProperties;

cp_SingleProperty
	: WS? implicitCallStmt_InStmt WS? EQ WS? '$'? cp_PropertyValue FRX_OFFSET? NEWLINE+
	;

cp_PropertyName
	: (OBJECT DOT)? ambiguousIdentifier (LPAREN literal RPAREN)? (DOT ambiguousIdentifier (LPAREN literal RPAREN)?)*
	;

cp_PropertyValue
    : DOLLAR? (literal | (LBRACE ambiguousIdentifier RBRACE) | POW ambiguousIdentifier)
    ;

cp_NestedProperty
	: WS? BEGINPROPERTY WS ambiguousIdentifier (LPAREN INTEGERLITERAL RPAREN)? (WS GUID)? NEWLINE+ (cp_Properties+)? ENDPROPERTY NEWLINE+
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
   : (lineNumber WS? COLON? WS?)? blockStmt (blockSep (lineNumber WS? COLON? WS?)? blockStmt)* blockSep?
   ;

// A statement separator: a newline, a colon, or any run of them.
//
// The colon USED to live in the NEWLINE lexer rule as `COLON ' '` — a colon counted only when a space
// followed it. That rejected `a = 1:b = 2`, `a::b`, a trailing colon, a line of only colons, and a colon
// before Else, all of which VB6 accepts and all of which are ordinary style. It cannot be fixed there:
// widening NEWLINE to swallow a bare colon deletes the very token `lineLabel` needs, so a label would
// stop being a label. Separating statements is a PARSER concern, so it moved here.
//
// The trailing `blockSep?` on `block` is what lets a body end on a separator — `Debug.Print 1:` and a
// procedure whose whole body is a colon.
blockSep
   : (WS? (NEWLINE | COLON) WS?) +
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
   | continueStmt
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
   | timeStmt
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

declareStmt
   : (visibility WS)? DECLARE WS (FUNCTION typeHint? | SUB) WS ambiguousIdentifier typeHint? WS LIB WS STRINGLITERAL (WS ALIAS WS STRINGLITERAL)? (WS? argList)? (WS asTypeClause)?
   ;

deftypeStmt
   : (DEFBOOL | DEFBYTE | DEFINT | DEFLNG | DEFCUR | DEFSNG | DEFDBL | DEFDEC | DEFDATE | DEFSTR | DEFOBJ | DEFVAR) WS letterrange (WS? COMMA WS? letterrange)*
   ;

deleteSettingStmt
   : DELETESETTING WS valueStmt WS? COMMA WS? valueStmt (WS? COMMA WS? valueStmt)?
   ;

doLoopStmt
   : DO blockSep (block blockSep)? LOOP # doBlockLoop
   | DO WS (WHILE | UNTIL) WS valueStmt blockSep (block blockSep)? LOOP # doWhileBlockLoop
   | DO blockSep (block blockSep) LOOP WS (WHILE | UNTIL) WS valueStmt # doBlockWhileLoop
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

continueStmt
   : CONTINUE_DO
   ;

filecopyStmt
   : FILECOPY WS valueStmt WS? COMMA WS? valueStmt
   ;

// `For _ Each` — WS? between the two keywords; see the note on selectCaseStmt for why that is not a widening.
forEachStmt
   : FOR WS? EACH WS ambiguousIdentifier typeHint? WS IN WS valueStmt blockSep (block blockSep)? NEXT (WS ambiguousIdentifier)?
   ;

forNextStmt
   : FOR WS iCS_S_VariableOrProcedureCall typeHint? (WS asTypeClause)? WS? EQ WS? valueStmt WS TO WS valueStmt (WS STEP WS valueStmt)? blockSep (block blockSep)? NEXT (WS ambiguousIdentifier typeHint?)?
   ;

functionStmt
   : (visibility WS)? (STATIC WS)? FUNCTION WS ambiguousIdentifier (WS? argList)? (WS asTypeClause)? blockSep (block blockSep)? END_FUNCTION
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

// One branch of a SINGLE-LINE If: a colon-joined run of statements, all of which belong to the branch.
//
// Measured, because the naive reading is wrong and wrong in the dangerous direction. In
// `If False Then A : B` VB6 runs NEITHER statement — the whole joined tail is the Then branch, not just
// the first. Treating B as unconditional would silently execute code the program said not to.
//
// A colon may also sit immediately before Else (`If c Then A : Else B`), so the trailing statement after a
// colon is optional. Newlines are deliberately NOT accepted here: that is what separates the single-line
// form from the block form.
inlineIfBody
   : (WS? COLON WS?)* blockStmt (WS? COLON (WS? blockStmt)?)*
   ;

ifThenElseStmt
   : IF WS ifConditionStmt WS THEN WS inlineIfBody (WS? ELSE WS inlineIfBody)? # inlineIfThenElse
   | ifBlockStmt ifElseIfBlockStmt* ifElseBlockStmt? END_IF # blockIfThenElse
   ;

ifBlockStmt
   : IF WS ifConditionStmt WS THEN blockSep (block blockSep)?
   ;

ifConditionStmt
   : valueStmt
   ;

ifElseIfBlockStmt
   : ELSEIF WS ifConditionStmt WS THEN blockSep (block blockSep)?
   ;

ifElseBlockStmt
   : ELSE blockSep (block blockSep)?
   ;

implementsStmt
   : IMPLEMENTS WS ambiguousIdentifier
   ;

inputStmt
   : INPUT WS valueStmt (WS? COMMA WS? valueStmt) +
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
   : MACRO_IF WS ifConditionStmt WS THEN NEWLINE + (moduleBody NEWLINE +)?
   ;

macroElseIfBlockStmt
   : MACRO_ELSEIF WS ifConditionStmt WS THEN NEWLINE + (moduleBody NEWLINE +)?
   ;

macroElseBlockStmt
   : MACRO_ELSE NEWLINE + (moduleBody NEWLINE +)?
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

// `Resume _ Next` — WS? between the two keywords; see the note on selectCaseStmt.
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
   : OPEN WS valueStmt WS FOR WS (APPEND | BINARY | INPUT | OUTPUT | RANDOM) (WS ACCESS WS (READ | WRITE | READ_WRITE))? (WS (SHARED | LOCK_READ | LOCK_WRITE | LOCK_READ_WRITE))? WS AS WS valueStmt (WS LEN WS? EQ WS? valueStmt)?
   ;

outputList
   : outputList_Expression (WS? (SEMICOLON | COMMA) WS? outputList_Expression?)*
   | outputList_Expression? (WS? (SEMICOLON | COMMA) WS? outputList_Expression?) +
   ;

outputList_Expression
   : (SPC | TAB) (WS? LPAREN WS? argsCall WS? RPAREN)?
   | valueStmt
   ;

printStmt
   : PRINT WS valueStmt WS? COMMA (WS? outputList)?
   ;

propertyGetStmt
   : (visibility WS)? (STATIC WS)? PROPERTY_GET WS ambiguousIdentifier typeHint? (WS? argList)? (WS asTypeClause)? blockSep (block blockSep)? END_PROPERTY
   ;

propertySetStmt
   : (visibility WS)? (STATIC WS)? PROPERTY_SET WS ambiguousIdentifier (WS? argList)? blockSep (block blockSep)? END_PROPERTY
   ;

propertyLetStmt
   : (visibility WS)? (STATIC WS)? PROPERTY_LET WS ambiguousIdentifier (WS? argList)? blockSep (block blockSep)? END_PROPERTY
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
   : RESUME (WS (NEXT | ambiguousIdentifier | INTEGERLITERAL))?
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
// invisible to it — the lexer has already hidden it. So the WS has to become optional.
//
// Optional is not a widening here. Two adjacent word-tokens cannot occur in the stream without something
// between them: `SelectCase` lexes as one IDENTIFIER, never as SELECT then CASE. So the only way the
// parser ever sees them adjacent is that a hidden-channel token — a continuation — sat between them,
// which is exactly the case being admitted. Applied ONLY where both neighbours are word-tokens; the
// literal-operand pairs (`Lib "x"`, `Option Base 1`) are lexically able to abut and so are not covered
// by this argument. They are recorded in the corpus as unmeasured rather than relaxed on a guess.
selectCaseStmt
   : SELECT WS? CASE WS valueStmt blockSep sC_Case* WS? END_SELECT
   ;

sC_Case
   : CASE WS sC_Cond WS? (COLON? NEWLINE* | blockSep) (block blockSep)?
   ;

// ELSE first, so that it is not interpreted as a variable call
sC_Cond
   : ELSE # caseCondElse
   | sC_CondExpr (WS? COMMA WS? sC_CondExpr)* #caseCondExpr
   ;

sC_CondExpr
   : IS WS? comparisonOperator WS? valueStmt # caseCondExprIs
   | valueStmt # caseCondExprValue
   | valueStmt WS TO WS valueStmt # caseCondExprTo
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
   : (visibility WS)? (STATIC WS)? SUB WS ambiguousIdentifier (WS? argList)? blockSep (block blockSep)? END_SUB
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
   : TYPEOF WS valueStmt (WS IS WS type)?
   ;

unloadStmt
   : UNLOAD WS valueStmt
   ;

unlockStmt
   : UNLOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?
   ;

// operator precedence is represented by rule order
valueStmt
   : literal                                                         # vsLiteral
   | LPAREN WS? valueStmt (WS? COMMA WS? valueStmt)* WS? RPAREN      # vsStruct
   | NEW WS valueStmt                                                # vsNew
   | typeOfStmt                                                      # vsTypeOf
   | ADDRESSOF WS valueStmt                                          # vsAddressOf
   | implicitCallStmt_InStmt WS? ASSIGN WS? valueStmt                # vsAssign
   | valueStmt WS? POW WS? valueStmt                                 # vsPow
   | MINUS WS? valueStmt                                             # vsNegation
   | PLUS WS? valueStmt                                              # vsPlus
   | valueStmt WS? DIV WS? valueStmt                                 # vsDiv
   | valueStmt WS? MULT WS? valueStmt                                # vsMult
   | valueStmt WS? MOD WS? valueStmt                                 # vsMod
   | valueStmt WS? PLUS WS? valueStmt                                # vsAdd
   | valueStmt WS? MINUS WS? valueStmt                               # vsMinus
   | valueStmt WS? AMPERSAND WS? valueStmt                           # vsAmp
   | valueStmt WS? EQ WS? valueStmt                                  # vsEq
   | valueStmt WS? NEQ WS? valueStmt                                 # vsNeq
   | valueStmt WS? LT WS? valueStmt                                  # vsLt
   | valueStmt WS? GT WS? valueStmt                                  # vsGt
   | valueStmt WS? LEQ WS? valueStmt                                 # vsLeq
   | valueStmt WS? GEQ WS? valueStmt                                 # vsGeq
   | valueStmt WS LIKE WS valueStmt                                  # vsLike
   | valueStmt WS IS WS valueStmt                                    # vsIs
   | NOT (WS valueStmt | LPAREN WS? valueStmt WS? RPAREN)            # vsNot
   | valueStmt WS? AND WS? valueStmt                                 # vsAnd
   | valueStmt WS? OR WS? valueStmt                                  # vsOr
   | valueStmt WS? XOR WS? valueStmt                                 # vsXor
   | valueStmt WS? EQV WS? valueStmt                                 # vsEqv
   | valueStmt WS? IMP WS? valueStmt                                 # vsImp
   | implicitCallStmt_InStmt                                         # vsICS
   | midStmt                                                         # vsMid
   ;

variableStmt
   : (DIM | STATIC | visibility) WS (WITHEVENTS WS)? variableListStmt
   ;

variableListStmt
   : variableSubStmt (WS? COMMA WS? variableSubStmt)*
   ;

variableSubStmt
   : ambiguousIdentifier typeHint? (WS? LPAREN WS? (subscripts WS?)? RPAREN WS?)? (WS asTypeClause)?
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
   : CALL WS implicitCallStmt_InStmt? DOT WS? ambiguousIdentifier typeHint? (WS? LPAREN WS? argsCall WS? RPAREN)?
   ;

implicitCallStmt_InBlock
   : iCS_B_ProcedureCall
   | iCS_B_MemberProcedureCall
   ;

// parantheses are forbidden in case of args
// variables cannot be called in blocks
// certainIdentifier instead of ambiguousIdentifier for preventing ambiguity with statement keywords
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
   : (ambiguousIdentifier | baseType | iCS_S_NestedProcedureCall) typeHint? WS? (LPAREN WS? (argsCall WS?)? RPAREN)+ dictionaryCallStmt?
   ;

iCS_S_NestedProcedureCall
	: ambiguousIdentifier typeHint? WS? LPAREN WS? (argsCall WS?)? RPAREN
	;


iCS_S_MembersCall
   : (iCS_S_VariableOrProcedureCall | iCS_S_ProcedureOrArrayCall)? iCS_S_MemberCall + dictionaryCallStmt?
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
   : (OPTIONAL WS)? ((BYVAL | BYREF) WS)? (PARAMARRAY WS)? ambiguousIdentifier typeHint? (WS? LPAREN WS? RPAREN)? (WS asTypeClause)? (WS? argDefaultValue)?
   ;

argDefaultValue
   : EQ WS? valueStmt
   ;

subscripts
   : subscript (WS? COMMA WS? subscript)*
   ;

subscript
   : (valueStmt WS TO WS)? valueStmt
   ;

// atomic rules ----------------------------------

ambiguousIdentifier
   : (IDENTIFIER | ambiguousKeyword) +
   | L_SQUARE_BRACKET (IDENTIFIER | ambiguousKeyword) + R_SQUARE_BRACKET
   ;

asTypeClause
   : AS WS (NEW WS)? type (WS fieldLength)?
   ;

baseType
   : BOOLEAN
   | BYTE
   | COLLECTION
   | CURRENCY
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
   | ambiguousKeyword (ambiguousKeyword | IDENTIFIER) +
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
   : MULT WS? (INTEGERLITERAL | ambiguousIdentifier)
   ;

letterrange
   : certainIdentifier (WS? MINUS WS? certainIdentifier)?
   ;

lineLabel
   : ambiguousIdentifier COLON
   ;

// A NUMERIC line label — `10 Debug.Print 1`. Distinct from lineLabel because it takes no colon and is a
// prefix on the same line rather than a statement of its own. VB6 Language Reference: line numbers are
// 0-2147483647. Ported from the LSP server's grammar, which already had it; the interpreter's lacking it
// made every module using numeric labels fail to PARSE, taking the whole file down rather than one
// statement.
lineNumber
   : INTEGERLITERAL
   ;

literal
   : HEXLITERAL
   | DATELITERAL
   | DOUBLELITERAL
   | FILENUMBER
   | INTEGERLITERAL
   | OCTALLITERAL
   | STRINGLITERAL
   | TRUE
   | FALSE
   | NOTHING
   | NULL
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

type
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
   | NULL
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

// lexer rules --------------------------------------------------------------------------------

// keywords

ACCESS
   : A C C E S S
   ;


ADDRESSOF
   : A D D R E S S O F
   ;


ALIAS
   : A L I A S
   ;


AND
   : A N D
   ;


ATTRIBUTE
   : A T T R I B U T E
   ;


APPACTIVATE
   : A P P A C T I V A T E
   ;


APPEND
   : A P P E N D
   ;


AS
   : A S
   ;


BEEP
   : B E E P
   ;


BEGIN
   : B E G I N
   ;


BEGINPROPERTY
   : B E G I N P R O P E R T Y
   ;


BINARY
   : B I N A R Y
   ;


BOOLEAN
   : B O O L E A N
   ;


BYVAL
   : B Y V A L
   ;


BYREF
   : B Y R E F
   ;


BYTE
   : B Y T E
   ;


CALL
   : C A L L
   ;


CASE
   : C A S E
   ;


CHDIR
   : C H D I R
   ;


CHDRIVE
   : C H D R I V E
   ;


CLASS
   : C L A S S
   ;


CLOSE
   : C L O S E
   ;


COLLECTION
   : C O L L E C T I O N
   ;


CONST
   : C O N S T
   ;


CURRENCY
   : C U R R E N C Y
   ;

DATE
   : D A T E
   ;


DECLARE
   : D E C L A R E
   ;


DEFBOOL
   : D E F B O O L
   ;


DEFBYTE
   : D E F B Y T E
   ;


DEFDATE
   : D E F D A T E
   ;


DEFDBL
   : D E F D B L
   ;


DEFDEC
   : D E F D E C
   ;


DEFCUR
   : D E F C U R
   ;


DEFINT
   : D E F I N T
   ;


DEFLNG
   : D E F L N G
   ;


DEFOBJ
   : D E F O B J
   ;


DEFSNG
   : D E F S N G
   ;


DEFSTR
   : D E F S T R
   ;


DEFVAR
   : D E F V A R
   ;


DELETESETTING
   : D E L E T E S E T T I N G
   ;


DIM
   : D I M
   ;


DO
   : D O
   ;


DOUBLE
   : D O U B L E
   ;


EACH
   : E A C H
   ;


ELSE
   : E L S E
   ;


ELSEIF
   : E L S E I F
   ;


END_ENUM
   : E N D KWSEP E N U M
   ;


END_FUNCTION
   : E N D KWSEP F U N C T I O N
   ;


END_IF
   : E N D KWSEP I F
   ;


END_PROPERTY
   : E N D KWSEP P R O P E R T Y
   ;


END_SELECT
   : E N D KWSEP S E L E C T
   ;


END_SUB
   : E N D KWSEP S U B
   ;


END_TYPE
   : E N D KWSEP T Y P E
   ;


END_WITH
   : E N D KWSEP W I T H
   ;


END
   : E N D
   ;


ENDPROPERTY
   : E N D P R O P E R T Y
   ;


EMPTY_
   : E M P T Y
   ;


ENUM
   : E N U M
   ;


EQV
   : E Q V
   ;


ERASE
   : E R A S E
   ;


ERROR
   : E R R O R
   ;


EVENT
   : E V E N T
   ;

CONTINUE_DO
    : C O N T I N U E ' ' D O
    ;

EXIT_DO
   : E X I T KWSEP D O
   ;


EXIT_FOR
   : E X I T KWSEP F O R
   ;


EXIT_FUNCTION
   : E X I T KWSEP F U N C T I O N
   ;


EXIT_PROPERTY
   : E X I T KWSEP P R O P E R T Y
   ;


EXIT_SUB
   : E X I T KWSEP S U B
   ;


FALSE
   : F A L S E
   ;


FILECOPY
   : F I L E C O P Y
   ;


FRIEND
   : F R I E N D
   ;


FOR
   : F O R
   ;


FUNCTION
   : F U N C T I O N
   ;


GET
   : G E T
   ;


GLOBAL
   : G L O B A L
   ;


GOSUB
   : G O S U B
   ;


GOTO
   : G O T O
   ;


IF
   : I F
   ;


IMP
   : I M P
   ;


IMPLEMENTS
   : I M P L E M E N T S
   ;


IN
   : I N
   ;


INPUT
   : I N P U T
   ;


IS
   : I S
   ;


INTEGER
   : I N T E G E R
   ;


KILL
   : K I L L
   ;


LOAD
   : L O A D
   ;


LOCK
   : L O C K
   ;


LONG
   : L O N G
   ;


LOOP
   : L O O P
   ;


LEN
   : L E N
   ;


LET
   : L E T
   ;


LIB
   : L I B
   ;


LIKE
   : L I K E
   ;


LINE_INPUT
   : L I N E KWSEP I N P U T
   ;


LOCK_READ
   : L O C K KWSEP R E A D
   ;


LOCK_WRITE
   : L O C K KWSEP W R I T E
   ;


LOCK_READ_WRITE
   : L O C K KWSEP R E A D KWSEP W R I T E
   ;


LSET
   : L S E T
   ;


MACRO_IF
   : HASH I F
   ;


MACRO_ELSEIF
   : HASH E L S E I F
   ;


MACRO_ELSE
   : HASH E L S E
   ;


MACRO_END_IF
   : HASH E N D KWSEP I F
   ;


ME
   : M E
   ;


MID
   : M I D
   ;


MKDIR
   : M K D I R
   ;


MOD
   : M O D
   ;


NAME
   : N A M E
   ;


NEXT
   : N E X T
   ;


NEW
   : N E W
   ;


NOT
   : N O T
   ;


NOTHING
   : N O T H I N G
   ;


NULL
   : N U L L
   ;

OBJECT
   : O B J E C T
   ;

ON
   : O N
   ;


ON_ERROR
   : O N KWSEP E R R O R
   ;


ON_LOCAL_ERROR
   : O N KWSEP L O C A L KWSEP E R R O R
   ;


OPEN
   : O P E N
   ;


OPTIONAL
   : O P T I O N A L
   ;


OPTION_BASE
   : O P T I O N KWSEP B A S E
   ;


OPTION_EXPLICIT
   : O P T I O N KWSEP E X P L I C I T
   ;


OPTION_COMPARE
   : O P T I O N KWSEP C O M P A R E
   ;


OPTION_PRIVATE_MODULE
   : O P T I O N KWSEP P R I V A T E KWSEP M O D U L E
   ;


OR
   : O R
   ;


OUTPUT
   : O U T P U T
   ;


PARAMARRAY
   : P A R A M A R R A Y
   ;


PRESERVE
   : P R E S E R V E
   ;


PRINT
   : P R I N T
   ;


PRIVATE
   : P R I V A T E
   ;


PROPERTY_GET
   : P R O P E R T Y KWSEP G E T
   ;


PROPERTY_LET
   : P R O P E R T Y KWSEP L E T
   ;


PROPERTY_SET
   : P R O P E R T Y KWSEP S E T
   ;


PUBLIC
   : P U B L I C
   ;


PUT
   : P U T
   ;


RANDOM
   : R A N D O M
   ;


RANDOMIZE
   : R A N D O M I Z E
   ;


RAISEEVENT
   : R A I S E E V E N T
   ;


READ
   : R E A D
   ;


READ_WRITE
   : R E A D KWSEP W R I T E
   ;


REDIM
   : R E D I M
   ;


REM
   : R E M
   ;


RESET
   : R E S E T
   ;


RESUME
   : R E S U M E
   ;


RETURN
   : R E T U R N
   ;


RMDIR
   : R M D I R
   ;


RSET
   : R S E T
   ;


SAVEPICTURE
   : S A V E P I C T U R E
   ;


SAVESETTING
   : S A V E S E T T I N G
   ;


SEEK
   : S E E K
   ;


SELECT
   : S E L E C T
   ;


SENDKEYS
   : S E N D K E Y S
   ;


SET
   : S E T
   ;


SETATTR
   : S E T A T T R
   ;


SHARED
   : S H A R E D
   ;


SINGLE
   : S I N G L E
   ;


SPC
   : S P C
   ;


STATIC
   : S T A T I C
   ;


STEP
   : S T E P
   ;


STOP
   : S T O P
   ;


STRING
   : S T R I N G
   ;


SUB
   : S U B
   ;


TAB
   : T A B
   ;


TEXT
   : T E X T
   ;


THEN
   : T H E N
   ;


TIME
   : T I M E
   ;


TO
   : T O
   ;


TRUE
   : T R U E
   ;


TYPE
   : T Y P E
   ;


TYPEOF
   : T Y P E O F
   ;


UNLOAD
   : U N L O A D
   ;


UNLOCK
   : U N L O C K
   ;


UNTIL
   : U N T I L
   ;


VARIANT
   : V A R I A N T
   ;


VERSION
   : V E R S I O N
   ;


WEND
   : W E N D
   ;


WHILE
   : W H I L E
   ;


WIDTH
   : W I D T H
   ;


WITH
   : W I T H
   ;


WITHEVENTS
   : W I T H E V E N T S
   ;


WRITE
   : W R I T E
   ;


XOR
   : X O R
   ;

// symbols

AMPERSAND
   : '&'
   ;


ASSIGN
   : ':='
   ;


AT
   : '@'
   ;


COLON
   : ':'
   ;


COMMA
   : ','
   ;


DIV
   : '\\' | '/'
   ;


DOLLAR
   : '$'
   ;


DOT
   : '.'
   ;


EQ
   : '='
   ;


EXCLAMATIONMARK
   : '!'
   ;


GEQ
   : '>='
   ;


GT
   : '>'
   ;


HASH
   : '#'
   ;


LEQ
   : '<='
   ;

LBRACE
	: '{'
	;


LPAREN
   : '('
   ;


LT
   : '<'
   ;


MINUS
   : '-'
   ;


MINUS_EQ
   : '-='
   ;


MULT
   : '*'
   ;


NEQ
   : '<>'
   ;


PERCENT
   : '%'
   ;


PLUS
   : '+'
   ;


PLUS_EQ
   : '+='
   ;


POW
   : '^'
   ;


RBRACE
	: '}'
	;


RPAREN
   : ')'
   ;


SEMICOLON
   : ';'
   ;


L_SQUARE_BRACKET
   : '['
   ;


R_SQUARE_BRACKET
   : ']'
   ;

// literals

STRINGLITERAL
   : '"' (~ ["\r\n] | '""')* '"'
   ;


DATELITERAL
   : HASH (~ [#\r\n])* HASH
   ;


HEXLITERAL
   : '&H' [0-9A-F] + (AMPERSAND | PERCENT)?
   ;


INTEGERLITERAL
   : (PLUS | MINUS)? ('0' .. '9') + (('e' | 'E') INTEGERLITERAL)* (HASH | AMPERSAND | EXCLAMATIONMARK | AT | PERCENT)?
   ;


DOUBLELITERAL
   : (PLUS | MINUS)? ('0' .. '9')* DOT ('0' .. '9') + (('e' | 'E') (PLUS | MINUS)? ('0' .. '9') +)* (HASH | AMPERSAND | EXCLAMATIONMARK | AT | PERCENT)?
   ;


FILENUMBER
   : HASH LETTERORDIGIT +
   ;

OCTALLITERAL
   : (PLUS | MINUS)? '&O' [0-7] + (AMPERSAND | PERCENT)?
   ;

// misc
// A companion-binary offset in a DESIGNER file: `Picture = "Form1.frx":0000`.
//
// It used to be `COLON [0-9A-F]+`, which is live in ordinary code too — and A-F are hex digits, so
// `Debug.Print "A":Debug.Print "B"` lexed `:D` as an offset and swallowed the statement separator. The
// same for `:b`, `:a`, `:F` and `Skip:Debug`. That single token was responsible for most of what the
// conformance corpus reported as label and separator failures.
//
// Narrowed to the shape VB6 actually writes: a leading DIGIT and at least four hex digits in total, which
// is what zero-padding guarantees. `:Debug`, `:b` and `:1a` no longer match; `:0000` and `:1A2B` still do.
// The residual risk is an offset into a .frx large enough to start with A-F (0xA0000 bytes and up); the
// round-trip corpus gate would catch that, and it is a far rarer shape than a colon before an identifier.
FRX_OFFSET
	: COLON [0-9] [0-9A-F] [0-9A-F] [0-9A-F]+
	;

GUID
	: LBRACE [0-9A-F]+ MINUS [0-9A-F]+ MINUS [0-9A-F]+ MINUS [0-9A-F]+ MINUS [0-9A-F]+ RBRACE
	;

// identifier

IDENTIFIER
   : LETTER LETTERORDIGIT*
   ;

// whitespace, line breaks, comments, ...

// A whitespace RUN before the underscore, and any whitespace after it. This used to demand exactly one
// space and nothing trailing, which rejected shapes that are everywhere in real VB6 — a tab before the
// underscore, and the multi-space alignment people use to line their continuations up. A rejected
// continuation is not a lost statement, it is a lost MODULE, which made this among the most damaging gaps
// the conformance corpus found. At least one space is still required: an underscore with none before it
// is a syntax error in VB6 too. Measured against vb6.exe; see corpus/continuation-and-separator.
LINE_CONTINUATION
   : [ \t]+ '_' [ \t]* '\r'? '\n' -> channel(HIDDEN)
   ;

// The separator INSIDE a multi-word keyword: End Sub, Exit For, On Error, Option Explicit.
//
// It used to be a single literal space, which refused two things VB6 accepts: extra spaces or a tab
// between the words, and a LINE CONTINUATION between them. `End _` / `Sub` is legal VB6 and was a
// parse failure here, which costs the whole module rather than one statement.
//
// The continuation is on the hidden channel, so it cannot separate the words from the parser's side;
// it has to be part of the token. Referencing LINE_CONTINUATION from inside another lexer rule
// matches its text without applying its channel command, which is exactly what is wanted.
fragment KWSEP
   : ( [ \t] | LINE_CONTINUATION ) +
   ;


// A newline only. The colon alternative moved to the parser rule `blockSep` — see the note there for
// why it could not stay: `COLON ' '` required a space after the colon, and widening it to a bare colon
// would consume the token `lineLabel` needs to be a label at all.
NEWLINE
   : WS? '\r'? '\n' WS?
   ;


COMMENT
   : WS? ('\'' | COLON? REM KWSEP) (LINE_CONTINUATION | ~ ('\n' | '\r'))* -> channel(HIDDEN)
   ;


WS
   : [ \t] +
   ;

// letters

fragment LETTER
   : [a-zA-Z_äöüÄÖÜáéíóúÁÉÍÓÚâêîôûÂÊÎÔÛàèìòùÀÈÌÒÙãẽĩõũÃẼĨÕŨçÇ]
   ;


fragment LETTERORDIGIT
   : [a-zA-Z0-9_äöüÄÖÜáéíóúÁÉÍÓÚâêîôûÂÊÎÔÛàèìòùÀÈÌÒÙãẽĩõũÃẼĨÕŨçÇ]
   ;

// case insensitive chars

fragment A
   : ('a' | 'A')
   ;


fragment B
   : ('b' | 'B')
   ;


fragment C
   : ('c' | 'C')
   ;


fragment D
   : ('d' | 'D')
   ;


fragment E
   : ('e' | 'E')
   ;


fragment F
   : ('f' | 'F')
   ;


fragment G
   : ('g' | 'G')
   ;


fragment H
   : ('h' | 'H')
   ;


fragment I
   : ('i' | 'I')
   ;


fragment J
   : ('j' | 'J')
   ;


fragment K
   : ('k' | 'K')
   ;


fragment L
   : ('l' | 'L')
   ;


fragment M
   : ('m' | 'M')
   ;


fragment N
   : ('n' | 'N')
   ;


fragment O
   : ('o' | 'O')
   ;


fragment P
   : ('p' | 'P')
   ;


fragment Q
   : ('q' | 'Q')
   ;


fragment R
   : ('r' | 'R')
   ;


fragment S
   : ('s' | 'S')
   ;


fragment T
   : ('t' | 'T')
   ;


fragment U
   : ('u' | 'U')
   ;


fragment V
   : ('v' | 'V')
   ;


fragment W
   : ('w' | 'W')
   ;


fragment X
   : ('x' | 'X')
   ;


fragment Y
   : ('y' | 'Y')
   ;


fragment Z
   : ('z' | 'Z')
   ;