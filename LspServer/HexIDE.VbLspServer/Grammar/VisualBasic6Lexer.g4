// $antlr-format alignTrailingComments true, columnLimit 150, maxEmptyLinesToKeep 1, reflowComments false, useTab false
// $antlr-format allowShortRulesOnASingleLine true, allowShortBlocksOnASingleLine true, minEmptyLines 0, alignSemicolons ownLine
// $antlr-format alignColons trailing, singleLineOverrulesHangingColon true, alignLexerCommands true, alignLabels true, alignTrailers true

lexer grammar VisualBasic6Lexer;

options {
    caseInsensitive = true;
}

// keywords

ACCESS: 'ACCESS';

ADDRESSOF: 'ADDRESSOF';

ALIAS: 'ALIAS';

AND: 'AND';

ATTRIBUTE: 'ATTRIBUTE';

APPACTIVATE: 'APPACTIVATE';

APPEND: 'APPEND';

AS: 'AS';

BEEP: 'BEEP';

BEGIN: 'BEGIN';

BEGINPROPERTY: 'BEGINPROPERTY';

BINARY: 'BINARY';

BOOLEAN: 'BOOLEAN';

BYVAL: 'BYVAL';

BYREF: 'BYREF';

BYTE: 'BYTE';

CALL: 'CALL';

CASE: 'CASE';

CHDIR: 'CHDIR';

CHDRIVE: 'CHDRIVE';

CLASS: 'CLASS';

CLOSE: 'CLOSE';

COLLECTION: 'COLLECTION';

CONST: 'CONST';

DATE: 'DATE';

DECLARE: 'DECLARE';

DEFBOOL: 'DEFBOOL';

DEFBYTE: 'DEFBYTE';

DEFDATE: 'DEFDATE';

DEFDBL: 'DEFDBL';

DEFDEC: 'DEFDEC';

DEFCUR: 'DEFCUR';

DEFINT: 'DEFINT';

DEFLNG: 'DEFLNG';

DEFOBJ: 'DEFOBJ';

DEFSNG: 'DEFSNG';

DEFSTR: 'DEFSTR';

DEFVAR: 'DEFVAR';

DELETESETTING: 'DELETESETTING';

DIM: 'DIM';

DO: 'DO';

DOUBLE: 'DOUBLE';

EACH: 'EACH';

ELSE: 'ELSE';

ELSEIF: 'ELSEIF';

END_ENUM: 'END' KWSEP 'ENUM';

END_FUNCTION: 'END' KWSEP 'FUNCTION';

END_IF: 'END' KWSEP 'IF';

END_PROPERTY: 'END' KWSEP 'PROPERTY';

END_SELECT: 'END' KWSEP 'SELECT';

END_SUB: 'END' KWSEP 'SUB';

END_TYPE: 'END' KWSEP 'TYPE';

END_WITH: 'END' KWSEP 'WITH';

END: 'END';

ENDPROPERTY: 'ENDPROPERTY';

ENUM: 'ENUM';

EMPTY_: 'EMPTY';

EQV: 'EQV';

ERASE: 'ERASE';

ERROR: 'ERROR';

EVENT: 'EVENT';

EXIT_DO: 'EXIT' KWSEP 'DO';

EXIT_FOR: 'EXIT' KWSEP 'FOR';

EXIT_FUNCTION: 'EXIT' KWSEP 'FUNCTION';

EXIT_PROPERTY: 'EXIT' KWSEP 'PROPERTY';

EXIT_SUB: 'EXIT' KWSEP 'SUB';

FALSE: 'FALSE';

FILECOPY: 'FILECOPY';

FRIEND: 'FRIEND';

FOR: 'FOR';

FUNCTION: 'FUNCTION';

GET: 'GET';

GLOBAL: 'GLOBAL';

GOSUB: 'GOSUB';

GOTO: 'GOTO';

IF: 'IF';

IMP: 'IMP';

IMPLEMENTS: 'IMPLEMENTS';

IN: 'IN';

INPUT: 'INPUT';

IS: 'IS';

INTEGER: 'INTEGER';

KILL: 'KILL';

LOAD: 'LOAD';

LOCK: 'LOCK';

LONG: 'LONG';

LOOP: 'LOOP';

LEN: 'LEN';

LET: 'LET';

LIB: 'LIB';

LIKE: 'LIKE';

LINE_INPUT: 'LINE' KWSEP 'INPUT';

LOCK_READ: 'LOCK' KWSEP 'READ';

LOCK_WRITE: 'LOCK' KWSEP 'WRITE';

LOCK_READ_WRITE: 'LOCK' KWSEP 'READ' KWSEP 'WRITE';

LSET: 'LSET';

MACRO_IF: HASH 'IF';

MACRO_ELSEIF: HASH 'ELSEIF';

MACRO_ELSE: HASH 'ELSE';

MACRO_END_IF: HASH 'END' KWSEP 'IF';

ME: 'ME';

MID: 'MID';

MKDIR: 'MKDIR';

MOD: 'MOD';

NAME: 'NAME';

NEXT: 'NEXT';

NEW: 'NEW';

NOT: 'NOT';

NOTHING: 'NOTHING';

NULL_: 'NULL';

OBJECT: 'OBJECT';

ON: 'ON';

ON_ERROR: 'ON' KWSEP 'ERROR';

ON_LOCAL_ERROR: 'ON' KWSEP 'LOCAL' KWSEP 'ERROR';

OPEN: 'OPEN';

OPTIONAL: 'OPTIONAL';

OPTION_BASE: 'OPTION' KWSEP 'BASE';

OPTION_EXPLICIT: 'OPTION' KWSEP 'EXPLICIT';

OPTION_COMPARE: 'OPTION' KWSEP 'COMPARE';

OPTION_PRIVATE_MODULE: 'OPTION' KWSEP 'PRIVATE' KWSEP 'MODULE';

OR: 'OR';

OUTPUT: 'OUTPUT';

PARAMARRAY: 'PARAMARRAY';

PRESERVE: 'PRESERVE';

PRINT: 'PRINT';

PRIVATE: 'PRIVATE';

PROPERTY_GET: 'PROPERTY' KWSEP 'GET';

PROPERTY_LET: 'PROPERTY' KWSEP 'LET';

PROPERTY_SET: 'PROPERTY' KWSEP 'SET';

PUBLIC: 'PUBLIC';

PUT: 'PUT';

RANDOM: 'RANDOM';

RANDOMIZE: 'RANDOMIZE';

RAISEEVENT: 'RAISEEVENT';

READ: 'READ';

READ_WRITE: 'READ' KWSEP 'WRITE';

REDIM: 'REDIM';

REM: 'REM';

RESET: 'RESET';

RESUME: 'RESUME';

RETURN: 'RETURN';

RMDIR: 'RMDIR';

RSET: 'RSET';

SAVEPICTURE: 'SAVEPICTURE';

SAVESETTING: 'SAVESETTING';

SEEK: 'SEEK';

SELECT: 'SELECT';

SENDKEYS: 'SENDKEYS';

SET: 'SET';

SETATTR: 'SETATTR';

SHARED: 'SHARED';

SINGLE: 'SINGLE';

SPC: 'SPC';

STATIC: 'STATIC';

STEP: 'STEP';

STOP: 'STOP';

STRING: 'STRING';

SUB: 'SUB';

TAB: 'TAB';

TEXT: 'TEXT';

THEN: 'THEN';

TIME: 'TIME';

TO: 'TO';

TRUE: 'TRUE';

TYPE: 'TYPE';

TYPEOF: 'TYPEOF';

UNLOAD: 'UNLOAD';

UNLOCK: 'UNLOCK';

UNTIL: 'UNTIL';

VARIANT: 'VARIANT';

VERSION: 'VERSION';

WEND: 'WEND';

WHILE: 'WHILE';

WIDTH: 'WIDTH';

WITH: 'WITH';

WITHEVENTS: 'WITHEVENTS';

WRITE: 'WRITE';

XOR: 'XOR';

// symbols

AMPERSAND: '&';

ASSIGN: ':=';

AT: '@';

COLON: ':';

COMMA: ',';

IDIV : '\\';
DIV  : '/';

DOLLAR: '$';

DOT: '.';

EQ: '=';

EXCLAMATIONMARK: '!';

GEQ: '>=';

GT: '>';

HASH: '#';

LEQ: '<=';

LBRACE: '{';

LPAREN: '(';

LT: '<';

MINUS: '-';

MINUS_EQ: '-=';

MULT: '*';

NEQ: '<>';

PERCENT: '%';

PLUS: '+';

PLUS_EQ: '+=';

POW: '^';

RBRACE: '}';

RPAREN: ')';

SEMICOLON: ';';

L_SQUARE_BRACKET: '[';

R_SQUARE_BRACKET: ']';

// literals

STRINGLITERAL: '"' (~ ["\r\n] | '""')* '"';

DATELITERAL: HASH (~ [#\r\n])* HASH;

COLORLITERAL: '&H' [0-9A-F]+ ( AMPERSAND | PERCENT )?;

// Clean-room: '%' (Integer) added to the numeric type-declaration suffix set (VB6 Language
// Reference, Type Declaration Characters). Not derived from any other grammar.
INTEGERLITERAL: [0-9]+ ('E' INTEGERLITERAL)* ( HASH | AMPERSAND | EXCLAMATIONMARK | AT | PERCENT)?;

DOUBLELITERAL:
    [0-9]* DOT [0-9]+ (('E' | 'D') (PLUS | MINUS)? [0-9]+)* (HASH | AMPERSAND | EXCLAMATIONMARK | AT)?
;

FILENUMBER: HASH LETTERORDIGIT+;

OCTALLITERAL: '&O' [0-7]+ ( AMPERSAND | PERCENT )?;

// misc
// A companion-binary offset in a DESIGNER file (`"Form1.frx":0000`). Narrowed from `COLON [0-9A-F]+`,
// which was live in ordinary code and — because A-F are hex digits — lexed the `:D` of
// `Debug.Print "A":Debug.Print "B"` as an offset, swallowing the statement separator. Mirrored from the
// interpreter's grammar; see the fuller note there.
FRX_OFFSET: COLON [0-9] [0-9A-F] [0-9A-F] [0-9A-F]+;

GUID: LBRACE [0-9A-F]+ MINUS [0-9A-F]+ MINUS [0-9A-F]+ MINUS [0-9A-F]+ MINUS [0-9A-F]+ RBRACE;

// identifier

IDENTIFIER: LETTER LETTERORDIGIT*;

// whitespace, line breaks, comments, ...

// A whitespace RUN before the underscore, and any whitespace after it — mirrored from the interpreter's
// grammar, where the conformance corpus showed the one-space form rejects a tab before the underscore and
// the multi-space alignment real VB6 is full of. Kept identical on purpose: GrammarParityTests exists
// because the two halves disagreeing about what VB6 is reaches users as an editor that accepts what the
// interpreter refuses, or the reverse.
LINE_CONTINUATION: [ \t]+ '_' [ \t]* '\r'? '\n' -> skip;

// The separator INSIDE a multi-word keyword. Mirrored from the interpreter's grammar: a single literal
// space refused both an aligning run of spaces and a line continuation between the two words, and
// `End _` / `Sub` is legal VB6. A continuation cannot do the separating from outside, because by the
// time the parser sees the tokens it has been skipped — so the keyword token has to absorb it itself.
fragment KWSEP: ([ \t] | LINE_CONTINUATION)+;

// A newline only. The colon alternative moved to the parser rule `blockSep` — see the note there. It
// could not stay here: `COLON ' '` demanded a space after the colon, and widening it to a bare colon
// consumes the token lineLabel needs to be a label at all.
NEWLINE: WS? '\r'? '\n' WS?;

COMMENT: WS? ('\'' | COLON? REM KWSEP) ( LINE_CONTINUATION | ~ ('\n' | '\r'))* -> skip;

WS: [ \t]+;

// letters

fragment LETTER: [A-Z_ÄÖÜÁÉÍÓÚÂÊÎÔÛÀÈÌÒÙÃẼĨÕŨÇ];

fragment LETTERORDIGIT: [A-Z0-9_ÄÖÜÁÉÍÓÚÂÊÎÔÛÀÈÌÒÙÃẼĨÕŨÇ];