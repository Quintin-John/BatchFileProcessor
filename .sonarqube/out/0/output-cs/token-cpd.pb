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
}HH Š
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

MessageProvenance 

Provenance '
{( )
get* -
;- .
}/ 0
public 

RecordLocator 
Locator  
{! "
get# &
;& '
}( )
public 


FieldValue 
	RawRecord 
{  !
get" %
;% &
}' (
public 

IReadOnlyList 
< 
RejectReason %
>% &
Reasons' .
{/ 0
get1 4
;4 5
}6 7
public(( 

RejectMessage(( 
((( 
string)) 
	messageId)) 
,)) 
MessageProvenance** 

provenance** $
,**$ %
RecordLocator++ 
locator++ 
,++ 

FieldValue,, 
	rawRecord,, 
,,, 
IReadOnlyList-- 
<-- 
RejectReason-- "
>--" #
reasons--$ +
)--+ ,
{.. 
ArgumentException// 
.// #
ThrowIfNullOrWhiteSpace// 1
(//1 2
	messageId//2 ;
)//; <
;//< =!
ArgumentNullException00 
.00 
ThrowIfNull00 )
(00) *

provenance00* 4
)004 5
;005 6!
ArgumentNullException11 
.11 
ThrowIfNull11 )
(11) *
locator11* 1
)111 2
;112 3!
ArgumentNullException22 
.22 
ThrowIfNull22 )
(22) *
	rawRecord22* 3
)223 4
;224 5!
ArgumentNullException33 
.33 
ThrowIfNull33 )
(33) *
reasons33* 1
)331 2
;332 3
if55 

(55 
reasons55 
.55 
Count55 
==55 
$num55 
)55 
{66 	
throw77 
new77 
ArgumentException77 '
(77' (
$str77( Q
,77Q R
nameof77S Y
(77Y Z
reasons77Z a
)77a b
)77b c
;77c d
}88 	
var:: 
copy:: 
=:: 
new:: 
List:: 
<:: 
RejectReason:: (
>::( )
(::) *
reasons::* 1
.::1 2
Count::2 7
)::7 8
;::8 9
foreach;; 
(;; 
var;; 
reason;; 
in;; 
reasons;; &
);;& '
{<< 	
if== 
(== 
reason== 
is== 
null== 
)== 
{>> 
throw?? 
new?? 
ArgumentException?? +
(??+ ,
$str??, U
,??U V
nameof??W ]
(??] ^
reasons??^ e
)??e f
)??f g
;??g h
}@@ 
copyBB 
.BB 
AddBB 
(BB 
reasonBB 
)BB 
;BB 
}CC 	
	MessageIdEE 
=EE 
	messageIdEE 
;EE 

ProvenanceFF 
=FF 

provenanceFF 
;FF  
LocatorGG 
=GG 
locatorGG 
;GG 
	RawRecordHH 
=HH 
	rawRecordHH 
;HH 
ReasonsII 
=II 
newII 
ReadOnlyCollectionII (
<II( )
RejectReasonII) 5
>II5 6
(II6 7
copyII7 ;
)II; <
;II< =
}JJ 
}KK ©
c/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/RecordLocator.cs
	namespace 	
Common
 
. 
	Messaging 
. 
	Contracts $
;$ %
public 
sealed 
record 
RecordLocator "
{ 
public

 

long

 
	RecordSeq

 
{

 
get

 
;

  
}

! "
public 

long 

ByteOffset 
{ 
get  
;  !
}" #
public 

string 

RecordType 
{ 
get "
;" #
}$ %
public 

RecordLocator 
( 
long 
	recordSeq '
,' (
long) -

byteOffset. 8
,8 9
string: @

recordTypeA K
)K L
{ '
ArgumentOutOfRangeException #
.# $
ThrowIfLessThan$ 3
(3 4
	recordSeq4 =
,= >
$num? @
)@ A
;A B'
ArgumentOutOfRangeException #
.# $
ThrowIfNegative$ 3
(3 4

byteOffset4 >
)> ?
;? @
ArgumentException 
. #
ThrowIfNullOrWhiteSpace 1
(1 2

recordType2 <
)< =
;= >
	RecordSeq 
= 
	recordSeq 
; 

ByteOffset 
= 

byteOffset 
;  

RecordType   
=   

recordType   
;    
}!! 
}"" æ
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
}$$ Ì
g/Users/quintin-johnsmith/Documents/Development/G266/src/Common.Messaging.Contracts/MessageProvenance.cs
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
record 
MessageProvenance &
{		 
public 

string 
CorrelationId 
{  !
get" %
;% &
}' (
public 

string 
FileId 
{ 
get 
; 
}  !
public 

string 
FileName 
{ 
get  
;  !
}" #
public 

string 
Profile 
{ 
get 
;  
}! "
public 

string 
LayoutVersion 
{  !
get" %
;% &
}' (
public   

MessageProvenance   
(   
string!! 
correlationId!! 
,!! 
string"" 
fileId"" 
,"" 
string## 
fileName## 
,## 
string$$ 
profile$$ 
,$$ 
string%% 
layoutVersion%% 
)%% 
{&& 
ArgumentException'' 
.'' #
ThrowIfNullOrWhiteSpace'' 1
(''1 2
correlationId''2 ?
)''? @
;''@ A
ArgumentException(( 
.(( #
ThrowIfNullOrWhiteSpace(( 1
(((1 2
fileId((2 8
)((8 9
;((9 :
ArgumentException)) 
.)) #
ThrowIfNullOrWhiteSpace)) 1
())1 2
fileName))2 :
))): ;
;)); <
ArgumentException** 
.** #
ThrowIfNullOrWhiteSpace** 1
(**1 2
profile**2 9
)**9 :
;**: ;
ArgumentException++ 
.++ #
ThrowIfNullOrWhiteSpace++ 1
(++1 2
layoutVersion++2 ?
)++? @
;++@ A
CorrelationId-- 
=-- 
correlationId-- %
;--% &
FileId.. 
=.. 
fileId.. 
;.. 
FileName// 
=// 
fileName// 
;// 
Profile00 
=00 
profile00 
;00 
LayoutVersion11 
=11 
layoutVersion11 %
;11% &
}22 
}33 œ
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

RecordLocator 
Locator  
{! "
get# &
;& '
}( )
public 

IReadOnlyDictionary 
< 
string %
,% &

FieldValue' 1
>1 2
Fields3 9
{: ;
get< ?
;? @
}A B
public 

IngestRecord 
( 
RecordLocator %
locator& -
,- .
IReadOnlyDictionary/ B
<B C
stringC I
,I J

FieldValueK U
>U V
fieldsW ]
)] ^
{ !
ArgumentNullException 
. 
ThrowIfNull )
() *
locator* 1
)1 2
;2 3!
ArgumentNullException 
. 
ThrowIfNull )
() *
fields* 0
)0 1
;1 2
var 
copy 
= 
new 

Dictionary !
<! "
string" (
,( )

FieldValue* 4
>4 5
(5 6
fields6 <
.< =
Count= B
,B C
StringComparerD R
.R S
OrdinalS Z
)Z [
;[ \
foreach 
( 
var 
pair 
in 
fields #
)# $
{   	
if!! 
(!! 
string!! 
.!! 
IsNullOrWhiteSpace!! )
(!!) *
pair!!* .
.!!. /
Key!!/ 2
)!!2 3
)!!3 4
{"" 
throw## 
new## 
ArgumentException## +
(##+ ,
$str##, L
,##L M
nameof##N T
(##T U
fields##U [
)##[ \
)##\ ]
;##] ^
}$$ 
if&& 
(&& 
pair&& 
.&& 
Value&& 
is&& 
null&& "
)&&" #
{'' 
throw(( 
new(( 
ArgumentException(( +
(((+ ,
$str((, L
,((L M
nameof((N T
(((T U
fields((U [
)(([ \
)((\ ]
;((] ^
})) 
copy++ 
[++ 
pair++ 
.++ 
Key++ 
]++ 
=++ 
pair++ !
.++! "
Value++" '
;++' (
},, 	
Locator.. 
=.. 
locator.. 
;.. 
Fields// 
=// 
new// 
ReadOnlyDictionary// '
<//' (
string//( .
,//. /

FieldValue//0 :
>//: ;
(//; <
copy//< @
)//@ A
;//A B
}00 
}11 ’&
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

MessageProvenance 

Provenance '
{( )
get* -
;- .
}/ 0
public 

long 
BatchSeq 
{ 
get 
; 
}  !
public 

IReadOnlyList 
< 
IngestRecord %
>% &
Records' .
{/ 0
get1 4
;4 5
}6 7
public 

int 
Count 
=> 
Records 
.  
Count  %
;% &
public 

long 
FirstRecordSeq 
{  
get! $
;$ %
}& '
public   

long   
LastRecordSeq   
{   
get    #
;  # $
}  % &
public** 

IngestBatchMessage** 
(** 
string++ 
	messageId++ 
,++ 
MessageProvenance,, 

provenance,, $
,,,$ %
long-- 
batchSeq-- 
,-- 
IReadOnlyList.. 
<.. 
IngestRecord.. "
>.." #
records..$ +
)..+ ,
{// 
ArgumentException00 
.00 #
ThrowIfNullOrWhiteSpace00 1
(001 2
	messageId002 ;
)00; <
;00< =!
ArgumentNullException11 
.11 
ThrowIfNull11 )
(11) *

provenance11* 4
)114 5
;115 6'
ArgumentOutOfRangeException22 #
.22# $
ThrowIfNegative22$ 3
(223 4
batchSeq224 <
)22< =
;22= >!
ArgumentNullException33 
.33 
ThrowIfNull33 )
(33) *
records33* 1
)331 2
;332 3
if55 

(55 
records55 
.55 
Count55 
==55 
$num55 
)55 
{66 	
throw77 
new77 
ArgumentException77 '
(77' (
$str77( S
,77S T
nameof77U [
(77[ \
records77\ c
)77c d
)77d e
;77e f
}88 	
var:: 
copy:: 
=:: 
new:: 
List:: 
<:: 
IngestRecord:: (
>::( )
(::) *
records::* 1
.::1 2
Count::2 7
)::7 8
;::8 9
var;; 
first;; 
=;; 
long;; 
.;; 
MaxValue;; !
;;;! "
var<< 
last<< 
=<< 
long<< 
.<< 
MinValue<<  
;<<  !
foreach== 
(== 
var== 
record== 
in== 
records== &
)==& '
{>> 	
if?? 
(?? 
record?? 
is?? 
null?? 
)?? 
{@@ 
throwAA 
newAA 
ArgumentExceptionAA +
(AA+ ,
$strAA, U
,AAU V
nameofAAW ]
(AA] ^
recordsAA^ e
)AAe f
)AAf g
;AAg h
}BB 
firstDD 
=DD 
MathDD 
.DD 
MinDD 
(DD 
firstDD "
,DD" #
recordDD$ *
.DD* +
LocatorDD+ 2
.DD2 3
	RecordSeqDD3 <
)DD< =
;DD= >
lastEE 
=EE 
MathEE 
.EE 
MaxEE 
(EE 
lastEE  
,EE  !
recordEE" (
.EE( )
LocatorEE) 0
.EE0 1
	RecordSeqEE1 :
)EE: ;
;EE; <
copyFF 
.FF 
AddFF 
(FF 
recordFF 
)FF 
;FF 
}GG 	
	MessageIdII 
=II 
	messageIdII 
;II 

ProvenanceJJ 
=JJ 

provenanceJJ 
;JJ  
BatchSeqKK 
=KK 
batchSeqKK 
;KK 
RecordsLL 
=LL 
newLL 
ReadOnlyCollectionLL (
<LL( )
IngestRecordLL) 5
>LL5 6
(LL6 7
copyLL7 ;
)LL; <
;LL< =
FirstRecordSeqMM 
=MM 
firstMM 
;MM 
LastRecordSeqNN 
=NN 
lastNN 
;NN 
}OO 
}PP Á

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