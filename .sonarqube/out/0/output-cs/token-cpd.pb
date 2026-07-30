Ð>
{/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/Serialization/FieldValueJsonConverter.cs
	namespace 	
Common
 
. 
	Messaging 
. 
	Contracts $
.$ %
Serialization% 2
;2 3
internal 
sealed	 
class #
FieldValueJsonConverter -
:. /
JsonConverter0 =
<= >

FieldValue> H
>H I
{ 
public 

override 
bool 

HandleNull #
=>$ &
true' +
;+ ,
public 

override 

FieldValue 
Read #
(# $
ref$ '
Utf8JsonReader( 6
reader7 =
,= >
Type? C
typeToConvertD Q
,Q R!
JsonSerializerOptionsS h
optionsi p
)p q
{ 
if 

( 
reader 
. 
	TokenType 
== 
JsonTokenType  -
.- .
StartObject. 9
)9 :
{ 	
var 
envelope 
= 
JsonSerializer )
.) *
Deserialize* 5
<5 6
EncryptedValue6 D
>D E
(E F
refF I
readerJ P
,P Q
optionsR Y
)Y Z
?? 
throw 
new 
JsonException *
(* +
$str+ U
)U V
;V W
return 
new 
EncryptedFieldValue *
(* +
envelope+ 3
)3 4
;4 5
} 	
return 
new 
ClearFieldValue "
(" #
ReadClearScalar# 2
(2 3
ref3 6
reader7 =
)= >
)> ?
;? @
} 
public   

override   
void   
Write   
(   
Utf8JsonWriter   -
writer  . 4
,  4 5

FieldValue  6 @
value  A F
,  F G!
JsonSerializerOptions  H ]
options  ^ e
)  e f
{!! !
ArgumentNullException"" 
."" 
ThrowIfNull"" )
("") *
writer""* 0
)""0 1
;""1 2!
ArgumentNullException## 
.## 
ThrowIfNull## )
(##) *
value##* /
)##/ 0
;##0 1
switch%% 
(%% 
value%% 
)%% 
{&& 	
case'' 
EncryptedFieldValue'' $
	encrypted''% .
:''. /
JsonSerializer(( 
.(( 
	Serialize(( (
(((( )
writer(() /
,((/ 0
	encrypted((1 :
.((: ;
Value((; @
,((@ A
options((B I
)((I J
;((J K
break)) 
;)) 
case** 
ClearFieldValue**  
clear**! &
:**& '
WriteClearScalar++  
(++  !
writer++! '
,++' (
clear++) .
.++. /
Value++/ 4
)++4 5
;++5 6
break,, 
;,, 
default-- 
:-- 
throw.. 
new.. 
JsonException.. '
(..' (
$"..( *
$str..* G
{..G H
value..H M
...M N
GetType..N U
(..U V
)..V W
}..W X
$str..X Z
"..Z [
)..[ \
;..\ ]
}// 	
}00 
private22 
static22 
object22 
?22 
ReadClearScalar22 *
(22* +
ref22+ .
Utf8JsonReader22/ =
reader22> D
)22D E
=>22F H
reader33 
.33 
	TokenType33 
switch33 
{44 	
JsonTokenType55 
.55 
String55  
=>55! #
reader55$ *
.55* +
	GetString55+ 4
(554 5
)555 6
,556 7
JsonTokenType66 
.66 
Number66  
=>66! #
reader66$ *
.66* +

GetDecimal66+ 5
(665 6
)666 7
,667 8
JsonTokenType77 
.77 
True77 
=>77 !
true77" &
,77& '
JsonTokenType88 
.88 
False88 
=>88  "
false88# (
,88( )
JsonTokenType99 
.99 
Null99 
=>99 !
null99" &
,99& '
_:: 
=>:: 
throw:: 
new:: 
JsonException:: (
(::( )
$"::) +
$str::+ J
{::J K
reader::K Q
.::Q R
	TokenType::R [
}::[ \
$str::\ ^
"::^ _
)::_ `
,::` a
};; 	
;;;	 

private== 
static== 
void== 
WriteClearScalar== (
(==( )
Utf8JsonWriter==) 7
writer==8 >
,==> ?
object==@ F
?==F G
value==H M
)==M N
{>> 
switch?? 
(?? 
value?? 
)?? 
{@@ 	
caseAA 
nullAA 
:AA 
writerBB 
.BB 
WriteNullValueBB %
(BB% &
)BB& '
;BB' (
breakCC 
;CC 
caseDD 
stringDD 
sDD 
:DD 
writerEE 
.EE 
WriteStringValueEE '
(EE' (
sEE( )
)EE) *
;EE* +
breakFF 
;FF 
caseGG 
boolGG 
bGG 
:GG 
writerHH 
.HH 
WriteBooleanValueHH (
(HH( )
bHH) *
)HH* +
;HH+ ,
breakII 
;II 
caseJJ 
decimalJJ 
mJJ 
:JJ 
writerKK 
.KK 
WriteNumberValueKK '
(KK' (
mKK( )
)KK) *
;KK* +
breakLL 
;LL 
caseMM 
longMM 
lMM 
:MM 
writerNN 
.NN 
WriteNumberValueNN '
(NN' (
lNN( )
)NN) *
;NN* +
breakOO 
;OO 
casePP 
intPP 
iPP 
:PP 
writerQQ 
.QQ 
WriteNumberValueQQ '
(QQ' (
iQQ( )
)QQ) *
;QQ* +
breakRR 
;RR 
caseSS 
doubleSS 
dSS 
:SS 
writerTT 
.TT 
WriteNumberValueTT '
(TT' (
dTT( )
)TT) *
;TT* +
breakUU 
;UU 
caseVV 
DateOnlyVV 
dateVV 
:VV 
writerWW 
.WW 
WriteStringValueWW '
(WW' (
dateWW( ,
.WW, -
ToStringWW- 5
(WW5 6
$strWW6 9
,WW9 :
CultureInfoWW; F
.WWF G
InvariantCultureWWG W
)WWW X
)WWX Y
;WWY Z
breakXX 
;XX 
caseYY 
DateTimeOffsetYY 
dtoYY  #
:YY# $
writerZZ 
.ZZ 
WriteStringValueZZ '
(ZZ' (
dtoZZ( +
.ZZ+ ,
ToStringZZ, 4
(ZZ4 5
$strZZ5 8
,ZZ8 9
CultureInfoZZ: E
.ZZE F
InvariantCultureZZF V
)ZZV W
)ZZW X
;ZZX Y
break[[ 
;[[ 
default\\ 
:\\ 
throw]] 
new]] 
JsonException]] '
(]]' (
$"]]( *
$str]]* N
{]]N O
value]]O T
.]]T U
GetType]]U \
(]]\ ]
)]]] ^
}]]^ _
$str]]_ a
"]]a b
)]]b c
;]]c d
}^^ 	
}__ 
}`` ·
b/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/RejectReason.cs
	namespace 	
Common
 
. 
	Messaging 
. 
	Contracts $
;$ %
public 
sealed 
record 
RejectReason !
{		 
public 

string 
Field 
{ 
get 
; 
}  
public 

string 
Rule 
{ 
get 
; 
} 
public 

string 
Code 
{ 
get 
; 
} 
public 

string 
? 
Expected 
{ 
get !
;! "
}# $
public 

string 
? 
Actual 
{ 
get 
;  
}! "
public 

int 
? 
Offset 
{ 
get 
; 
} 
public 

int 
? 
Length 
{ 
get 
; 
} 
public)) 

RejectReason)) 
()) 
string** 
field** 
,** 
string++ 
rule++ 
,++ 
string,, 
code,, 
,,, 
string-- 
?-- 
expected-- 
=-- 
null-- 
,--  
string.. 
?.. 
actual.. 
=.. 
null.. 
,.. 
int// 
?// 
offset// 
=// 
null// 
,// 
int00 
?00 
length00 
=00 
null00 
)00 
{11 
ArgumentException22 
.22 #
ThrowIfNullOrWhiteSpace22 1
(221 2
field222 7
)227 8
;228 9
ArgumentException33 
.33 #
ThrowIfNullOrWhiteSpace33 1
(331 2
rule332 6
)336 7
;337 8
ArgumentException44 
.44 #
ThrowIfNullOrWhiteSpace44 1
(441 2
code442 6
)446 7
;447 8
if66 

(66 
offset66 
is66 
<66 
$num66 
)66 
{77 	
throw88 
new88 '
ArgumentOutOfRangeException88 1
(881 2
nameof882 8
(888 9
offset889 ?
)88? @
,88@ A
offset88B H
,88H I
$str88J h
)88h i
;88i j
}99 	
if;; 

(;; 
length;; 
is;; 
<;; 
$num;; 
);; 
{<< 	
throw== 
new== '
ArgumentOutOfRangeException== 1
(==1 2
nameof==2 8
(==8 9
length==9 ?
)==? @
,==@ A
length==B H
,==H I
$str==J d
)==d e
;==e f
}>> 	
Field@@ 
=@@ 
field@@ 
;@@ 
RuleAA 
=AA 
ruleAA 
;AA 
CodeBB 
=BB 
codeBB 
;BB 
ExpectedCC 
=CC 
expectedCC 
;CC 
ActualDD 
=DD 
actualDD 
;DD 
OffsetEE 
=EE 
offsetEE 
;EE 
LengthFF 
=FF 
lengthFF 
;FF 
}GG 
}HH È2
c/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/RejectMessage.cs
	namespace 	
Common
 
. 
	Messaging 
. 
	Contracts $
;$ %
public 
sealed 
class 
RejectMessage !
{ 
public 

string 
	MessageId 
{ 
get !
;! "
}# $
public 

string 
CorrelationId 
{  !
get" %
;% &
}' (
public 

string 
FileId 
{ 
get 
; 
}  !
public 

string 
FileName 
{ 
get  
;  !
}" #
public 

string 
Profile 
{ 
get 
;  
}! "
public 

string 
LayoutVersion 
{  !
get" %
;% &
}' (
public   

long   
	RecordSeq   
{   
get   
;    
}  ! "
public## 

long## 

ByteOffset## 
{## 
get##  
;##  !
}##" #
public&& 

string&& 

RecordType&& 
{&& 
get&& "
;&&" #
}&&$ %
public-- 


FieldValue-- 
	RawRecord-- 
{--  !
get--" %
;--% &
}--' (
public00 

IReadOnlyList00 
<00 
RejectReason00 %
>00% &
Reasons00' .
{00/ 0
get001 4
;004 5
}006 7
publicAA 

RejectMessageAA 
(AA 
stringBB 
	messageIdBB 
,BB 
stringCC 
correlationIdCC 
,CC 
stringDD 
fileIdDD 
,DD 
stringEE 
fileNameEE 
,EE 
stringFF 
profileFF 
,FF 
stringGG 
layoutVersionGG 
,GG 
longHH 
	recordSeqHH 
,HH 
longII 

byteOffsetII 
,II 
stringJJ 

recordTypeJJ 
,JJ 

FieldValueKK 
	rawRecordKK 
,KK 
IReadOnlyListLL 
<LL 
RejectReasonLL "
>LL" #
reasonsLL$ +
)LL+ ,
{MM 
ArgumentExceptionNN 
.NN #
ThrowIfNullOrWhiteSpaceNN 1
(NN1 2
	messageIdNN2 ;
)NN; <
;NN< =
ArgumentExceptionOO 
.OO #
ThrowIfNullOrWhiteSpaceOO 1
(OO1 2
correlationIdOO2 ?
)OO? @
;OO@ A
ArgumentExceptionPP 
.PP #
ThrowIfNullOrWhiteSpacePP 1
(PP1 2
fileIdPP2 8
)PP8 9
;PP9 :
ArgumentExceptionQQ 
.QQ #
ThrowIfNullOrWhiteSpaceQQ 1
(QQ1 2
fileNameQQ2 :
)QQ: ;
;QQ; <
ArgumentExceptionRR 
.RR #
ThrowIfNullOrWhiteSpaceRR 1
(RR1 2
profileRR2 9
)RR9 :
;RR: ;
ArgumentExceptionSS 
.SS #
ThrowIfNullOrWhiteSpaceSS 1
(SS1 2
layoutVersionSS2 ?
)SS? @
;SS@ A'
ArgumentOutOfRangeExceptionTT #
.TT# $
ThrowIfLessThanTT$ 3
(TT3 4
	recordSeqTT4 =
,TT= >
$numTT? @
)TT@ A
;TTA B'
ArgumentOutOfRangeExceptionUU #
.UU# $
ThrowIfNegativeUU$ 3
(UU3 4

byteOffsetUU4 >
)UU> ?
;UU? @
ArgumentExceptionVV 
.VV #
ThrowIfNullOrWhiteSpaceVV 1
(VV1 2

recordTypeVV2 <
)VV< =
;VV= >!
ArgumentNullExceptionWW 
.WW 
ThrowIfNullWW )
(WW) *
	rawRecordWW* 3
)WW3 4
;WW4 5!
ArgumentNullExceptionXX 
.XX 
ThrowIfNullXX )
(XX) *
reasonsXX* 1
)XX1 2
;XX2 3
ifZZ 

(ZZ 
reasonsZZ 
.ZZ 
CountZZ 
==ZZ 
$numZZ 
)ZZ 
{[[ 	
throw\\ 
new\\ 
ArgumentException\\ '
(\\' (
$str\\( Q
,\\Q R
nameof\\S Y
(\\Y Z
reasons\\Z a
)\\a b
)\\b c
;\\c d
}]] 	
var__ 
copy__ 
=__ 
new__ 
List__ 
<__ 
RejectReason__ (
>__( )
(__) *
reasons__* 1
.__1 2
Count__2 7
)__7 8
;__8 9
foreach`` 
(`` 
var`` 
reason`` 
in`` 
reasons`` &
)``& '
{aa 	
ifbb 
(bb 
reasonbb 
isbb 
nullbb 
)bb 
{cc 
throwdd 
newdd 
ArgumentExceptiondd +
(dd+ ,
$strdd, U
,ddU V
nameofddW ]
(dd] ^
reasonsdd^ e
)dde f
)ddf g
;ddg h
}ee 
copygg 
.gg 
Addgg 
(gg 
reasongg 
)gg 
;gg 
}hh 	
	MessageIdjj 
=jj 
	messageIdjj 
;jj 
CorrelationIdkk 
=kk 
correlationIdkk %
;kk% &
FileIdll 
=ll 
fileIdll 
;ll 
FileNamemm 
=mm 
fileNamemm 
;mm 
Profilenn 
=nn 
profilenn 
;nn 
LayoutVersionoo 
=oo 
layoutVersionoo %
;oo% &
	RecordSeqpp 
=pp 
	recordSeqpp 
;pp 

ByteOffsetqq 
=qq 

byteOffsetqq 
;qq  

RecordTyperr 
=rr 

recordTyperr 
;rr  
	RawRecordss 
=ss 
	rawRecordss 
;ss 
Reasonstt 
=tt 
newtt 
ReadOnlyCollectiontt (
<tt( )
RejectReasontt) 5
>tt5 6
(tt6 7
copytt7 ;
)tt; <
;tt< =
}uu 
}vv æ
c/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/MessagingJson.cs
	namespace 	
Common
 
. 
	Messaging 
. 
	Contracts $
;$ %
public 
static 
class 
MessagingJson !
{ 
public 

static !
JsonSerializerOptions '
Options( /
{0 1
get2 5
;5 6
}7 8
=9 :
CreateOptions; H
(H I
)I J
;J K
private 
static !
JsonSerializerOptions (
CreateOptions) 6
(6 7
)7 8
{ 
var 
options 
= 
new !
JsonSerializerOptions /
{ 	 
PropertyNamingPolicy  
=! "
JsonNamingPolicy# 3
.3 4
	CamelCase4 =
,= >"
DefaultIgnoreCondition "
=# $
JsonIgnoreCondition% 8
.8 9
WhenWritingNull9 H
,H I
Encoder 
= 
JavaScriptEncoder '
.' (%
UnsafeRelaxedJsonEscaping( A
,A B
} 	
;	 

options   
.   

Converters   
.   
Add   
(   
new   "#
FieldValueJsonConverter  # :
(  : ;
)  ; <
)  < =
;  = >
options!! 
.!! 
MakeReadOnly!! 
(!! #
populateMissingResolver!! 4
:!!4 5
true!!6 :
)!!: ;
;!!; <
return"" 
options"" 
;"" 
}## 
}$$ û
b/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/IngestRecord.cs
	namespace 	
Common
 
. 
	Messaging 
. 
	Contracts $
;$ %
public 
sealed 
class 
IngestRecord  
{ 
public 

long 
	RecordSeq 
{ 
get 
;  
}! "
public 

long 

ByteOffset 
{ 
get  
;  !
}" #
public 

string 

RecordType 
{ 
get "
;" #
}$ %
public 

IReadOnlyDictionary 
< 
string %
,% &

FieldValue' 1
>1 2
Fields3 9
{: ;
get< ?
;? @
}A B
public"" 

IngestRecord"" 
("" 
long## 
	recordSeq## 
,## 
long$$ 

byteOffset$$ 
,$$ 
string%% 

recordType%% 
,%% 
IReadOnlyDictionary&& 
<&& 
string&& "
,&&" #

FieldValue&&$ .
>&&. /
fields&&0 6
)&&6 7
{'' '
ArgumentOutOfRangeException(( #
.((# $
ThrowIfLessThan(($ 3
(((3 4
	recordSeq((4 =
,((= >
$num((? @
)((@ A
;((A B'
ArgumentOutOfRangeException)) #
.))# $
ThrowIfNegative))$ 3
())3 4

byteOffset))4 >
)))> ?
;))? @
ArgumentException** 
.** #
ThrowIfNullOrWhiteSpace** 1
(**1 2

recordType**2 <
)**< =
;**= >!
ArgumentNullException++ 
.++ 
ThrowIfNull++ )
(++) *
fields++* 0
)++0 1
;++1 2
var-- 
copy-- 
=-- 
new-- 

Dictionary-- !
<--! "
string--" (
,--( )

FieldValue--* 4
>--4 5
(--5 6
fields--6 <
.--< =
Count--= B
,--B C
StringComparer--D R
.--R S
Ordinal--S Z
)--Z [
;--[ \
foreach.. 
(.. 
var.. 
pair.. 
in.. 
fields.. #
)..# $
{// 	
if00 
(00 
string00 
.00 
IsNullOrWhiteSpace00 )
(00) *
pair00* .
.00. /
Key00/ 2
)002 3
)003 4
{11 
throw22 
new22 
ArgumentException22 +
(22+ ,
$str22, L
,22L M
nameof22N T
(22T U
fields22U [
)22[ \
)22\ ]
;22] ^
}33 
if55 
(55 
pair55 
.55 
Value55 
is55 
null55 "
)55" #
{66 
throw77 
new77 
ArgumentException77 +
(77+ ,
$str77, L
,77L M
nameof77N T
(77T U
fields77U [
)77[ \
)77\ ]
;77] ^
}88 
copy:: 
[:: 
pair:: 
.:: 
Key:: 
]:: 
=:: 
pair:: !
.::! "
Value::" '
;::' (
};; 	
	RecordSeq== 
=== 
	recordSeq== 
;== 

ByteOffset>> 
=>> 

byteOffset>> 
;>>  

RecordType?? 
=?? 

recordType?? 
;??  
Fields@@ 
=@@ 
new@@ 
ReadOnlyDictionary@@ '
<@@' (
string@@( .
,@@. /

FieldValue@@0 :
>@@: ;
(@@; <
copy@@< @
)@@@ A
;@@A B
}AA 
}BB ©2
h/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/IngestBatchMessage.cs
	namespace 	
Common
 
. 
	Messaging 
. 
	Contracts $
;$ %
public 
sealed 
class 
IngestBatchMessage &
{ 
public 

string 
	MessageId 
{ 
get !
;! "
}# $
public 

string 
CorrelationId 
{  !
get" %
;% &
}' (
public 

string 
FileId 
{ 
get 
; 
}  !
public 

string 
FileName 
{ 
get  
;  !
}" #
public 

string 
Profile 
{ 
get 
;  
}! "
public 

string 
LayoutVersion 
{  !
get" %
;% &
}' (
public   

long   
BatchSeq   
{   
get   
;   
}    !
public## 

IReadOnlyList## 
<## 
IngestRecord## %
>##% &
Records##' .
{##/ 0
get##1 4
;##4 5
}##6 7
public&& 

int&& 
Count&& 
=>&& 
Records&& 
.&&  
Count&&  %
;&&% &
public)) 

long)) 
FirstRecordSeq)) 
{))  
get))! $
;))$ %
}))& '
public,, 

long,, 
LastRecordSeq,, 
{,, 
get,,  #
;,,# $
},,% &
public:: 

IngestBatchMessage:: 
(:: 
string;; 
	messageId;; 
,;; 
string<< 
correlationId<< 
,<< 
string== 
fileId== 
,== 
string>> 
fileName>> 
,>> 
string?? 
profile?? 
,?? 
string@@ 
layoutVersion@@ 
,@@ 
longAA 
batchSeqAA 
,AA 
IReadOnlyListBB 
<BB 
IngestRecordBB "
>BB" #
recordsBB$ +
)BB+ ,
{CC 
ArgumentExceptionDD 
.DD #
ThrowIfNullOrWhiteSpaceDD 1
(DD1 2
	messageIdDD2 ;
)DD; <
;DD< =
ArgumentExceptionEE 
.EE #
ThrowIfNullOrWhiteSpaceEE 1
(EE1 2
correlationIdEE2 ?
)EE? @
;EE@ A
ArgumentExceptionFF 
.FF #
ThrowIfNullOrWhiteSpaceFF 1
(FF1 2
fileIdFF2 8
)FF8 9
;FF9 :
ArgumentExceptionGG 
.GG #
ThrowIfNullOrWhiteSpaceGG 1
(GG1 2
fileNameGG2 :
)GG: ;
;GG; <
ArgumentExceptionHH 
.HH #
ThrowIfNullOrWhiteSpaceHH 1
(HH1 2
profileHH2 9
)HH9 :
;HH: ;
ArgumentExceptionII 
.II #
ThrowIfNullOrWhiteSpaceII 1
(II1 2
layoutVersionII2 ?
)II? @
;II@ A'
ArgumentOutOfRangeExceptionJJ #
.JJ# $
ThrowIfNegativeJJ$ 3
(JJ3 4
batchSeqJJ4 <
)JJ< =
;JJ= >!
ArgumentNullExceptionKK 
.KK 
ThrowIfNullKK )
(KK) *
recordsKK* 1
)KK1 2
;KK2 3
ifMM 

(MM 
recordsMM 
.MM 
CountMM 
==MM 
$numMM 
)MM 
{NN 	
throwOO 
newOO 
ArgumentExceptionOO '
(OO' (
$strOO( S
,OOS T
nameofOOU [
(OO[ \
recordsOO\ c
)OOc d
)OOd e
;OOe f
}PP 	
varRR 
copyRR 
=RR 
newRR 
ListRR 
<RR 
IngestRecordRR (
>RR( )
(RR) *
recordsRR* 1
.RR1 2
CountRR2 7
)RR7 8
;RR8 9
varSS 
firstSS 
=SS 
longSS 
.SS 
MaxValueSS !
;SS! "
varTT 
lastTT 
=TT 
longTT 
.TT 
MinValueTT  
;TT  !
foreachUU 
(UU 
varUU 
recordUU 
inUU 
recordsUU &
)UU& '
{VV 	
ifWW 
(WW 
recordWW 
isWW 
nullWW 
)WW 
{XX 
throwYY 
newYY 
ArgumentExceptionYY +
(YY+ ,
$strYY, U
,YYU V
nameofYYW ]
(YY] ^
recordsYY^ e
)YYe f
)YYf g
;YYg h
}ZZ 
first\\ 
=\\ 
Math\\ 
.\\ 
Min\\ 
(\\ 
first\\ "
,\\" #
record\\$ *
.\\* +
	RecordSeq\\+ 4
)\\4 5
;\\5 6
last]] 
=]] 
Math]] 
.]] 
Max]] 
(]] 
last]]  
,]]  !
record]]" (
.]]( )
	RecordSeq]]) 2
)]]2 3
;]]3 4
copy^^ 
.^^ 
Add^^ 
(^^ 
record^^ 
)^^ 
;^^ 
}__ 	
	MessageIdaa 
=aa 
	messageIdaa 
;aa 
CorrelationIdbb 
=bb 
correlationIdbb %
;bb% &
FileIdcc 
=cc 
fileIdcc 
;cc 
FileNamedd 
=dd 
fileNamedd 
;dd 
Profileee 
=ee 
profileee 
;ee 
LayoutVersionff 
=ff 
layoutVersionff %
;ff% &
BatchSeqgg 
=gg 
batchSeqgg 
;gg 
Recordshh 
=hh 
newhh 
ReadOnlyCollectionhh (
<hh( )
IngestRecordhh) 5
>hh5 6
(hh6 7
copyhh7 ;
)hh; <
;hh< =
FirstRecordSeqii 
=ii 
firstii 
;ii 
LastRecordSeqjj 
=jj 
lastjj 
;jj 
}kk 
}ll Á

`/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/FieldValue.cs
	namespace 	
Common
 
. 
	Messaging 
. 
	Contracts $
;$ %
public 
abstract 
record 

FieldValue !
{		 
private 
	protected 

FieldValue  
(  !
)! "
{ 
} 
} 
public 
sealed 
record 
ClearFieldValue $
($ %
object% +
?+ ,
Value- 2
)2 3
:4 5

FieldValue6 @
;@ A
public 
sealed 
record 
EncryptedFieldValue (
:) *

FieldValue+ 5
{ 
public 

EncryptedValue 
Value 
{  !
get" %
;% &
}' (
public"" 

EncryptedFieldValue"" 
("" 
EncryptedValue"" -
value"". 3
)""3 4
{## !
ArgumentNullException$$ 
.$$ 
ThrowIfNull$$ )
($$) *
value$$* /
)$$/ 0
;$$0 1
Value%% 
=%% 
value%% 
;%% 
}&& 
}'' —
d/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/EncryptedValue.cs
	namespace 	
Common
 
. 
	Messaging 
. 
	Contracts $
;$ %
public

 
sealed

 
record

 
EncryptedValue

 #
{ 
public 

string 
	Algorithm 
{ 
get !
;! "
}# $
public 

string 
KeyId 
{ 
get 
; 
}  
public 

string 

KeyVersion 
{ 
get "
;" #
}$ %
public 

string 
Nonce 
{ 
get 
; 
}  
public 

string 

Ciphertext 
{ 
get "
;" #
}$ %
public 

string 
Tag 
{ 
get 
; 
} 
public)) 

EncryptedValue)) 
()) 
string** 
	algorithm** 
,** 
string++ 
keyId++ 
,++ 
string,, 

keyVersion,, 
,,, 
string-- 
nonce-- 
,-- 
string.. 

ciphertext.. 
,.. 
string// 
tag// 
)// 
{00 
ArgumentException11 
.11 #
ThrowIfNullOrWhiteSpace11 1
(111 2
	algorithm112 ;
)11; <
;11< =
ArgumentException22 
.22 #
ThrowIfNullOrWhiteSpace22 1
(221 2
keyId222 7
)227 8
;228 9
ArgumentException33 
.33 #
ThrowIfNullOrWhiteSpace33 1
(331 2

keyVersion332 <
)33< =
;33= >
ArgumentException44 
.44 #
ThrowIfNullOrWhiteSpace44 1
(441 2
nonce442 7
)447 8
;448 9
ArgumentException55 
.55 #
ThrowIfNullOrWhiteSpace55 1
(551 2

ciphertext552 <
)55< =
;55= >
ArgumentException66 
.66 #
ThrowIfNullOrWhiteSpace66 1
(661 2
tag662 5
)665 6
;666 7
	Algorithm88 
=88 
	algorithm88 
;88 
KeyId99 
=99 
keyId99 
;99 

KeyVersion:: 
=:: 

keyVersion:: 
;::  
Nonce;; 
=;; 
nonce;; 
;;; 

Ciphertext<< 
=<< 

ciphertext<< 
;<<  
Tag== 
=== 
tag== 
;== 
}>> 
}?? 