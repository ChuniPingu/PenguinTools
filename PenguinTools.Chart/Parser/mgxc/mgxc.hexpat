#pragma author Foahh

import std.sys;

struct Field {
    s16 type;

    if (type == 0x04) {
        s16 attr;
        char data[attr];
    }
    if (type == 0x03) {
        s16 attr;
        double data;
    }
    if (type == 0x02) {
        s16 attr;
        s32 data;
    }
    if (type == 0x01) {
        s16 data;
    }
};

struct WideField {
    s32 type;
    s32 attr;
    char data[attr];
};

struct Meta {
    char name[4];
    Field data;
};

struct Event {
    char name[4];

    if (name == "beat") {
        Field bar;
        Field numerator;
        Field denominator;
    }

    if (name == "bpm ") {
        Field tick;
        Field bpm;
    }

    if (name == "til ") {
        Field tilId;
        Field tick;
        Field speed;
    }

    if (name == "bmrk") {
        WideField id;
        Field tick;
        WideField tag;
        WideField rgb;
    }

    if (name == "mbkm") {
        Field beat;
    }

    if (name == "smod") {
        Field tick;
        Field speed;
    }

    if (name == "rimg") {
        Field;
        Field;
        WideField;
    }

    s32;
};

enum NoteType : s8 {
    unk         = 0x00,
    Tap         = 0x01,
    ExTap       = 0x02,
    Flick       = 0x03,
    Damage      = 0x04,
    Hold        = 0x05,
    Slide       = 0x06,
    Air         = 0x07,
    AirHold     = 0x08,
    AirSlide    = 0x09,
    AirCrush    = 0x0A,
    Click       = 0x0B,
    Last        = 0x0D,
};

enum LongAttr : s8 {
    None          = 0x00,
    Begin         = 0x01,
    Step          = 0x02,
    Control       = 0x03,
    CurveControl  = 0x04,
    End           = 0x05,
    EndNoAct      = 0x06
};

enum Direction : s8 {
    None          = 0x00,
    Auto          = 0x01,
    Up            = 0x02,
    Down          = 0x03,
    Center        = 0x04,
    Left          = 0x05,
    Right         = 0x06,
    UpLeft        = 0x07,
    UpRight       = 0x08,
    DownLeft      = 0x09,
    DownRight     = 0x0A,
    RotateLeft    = 0x0B,
    RotateRight   = 0x0C,
    InOut         = 0x0D,
    OutIn         = 0x0E
};

enum ExAttr : s8 {
    None      = 0x00,
    Invert    = 0x01,
    HasNote   = 0x02,
    ExJdg     = 0x03,
};

enum OptionValue : s32 {
    AirCrushTracelike = 0x00,
    AirCrushDensity   = 0x01 ... 0x7FFFFFFE,
    AirCrushHeadOnly  = 0x7FFFFFFF,
};

struct Note {
    NoteType type;
    LongAttr longAttr;
    Direction direction;
    ExAttr exAttr;
    s8 variationId;
    s8 x;
    s16 width;
    s32 height;
    s32 tick;
    s32 timelineId;

    if (type == NoteType::AirCrush && longAttr == LongAttr::Begin) {
        OptionValue optionValue;
    }
};

#define ERROR "ERROR"

u16 NUM_META = 40;
u16 NUM_EVNT = 4;
u16 NUM_NOTE = 0;

struct Mgxc {
    char header[4];          // 4D 47 58 43
    std::assert(header == "MGXC", ERROR);

    s32 mgxcBlockSize;
    s32 version;             // 02 00 00 00

    char metaHeader[4];      // 6D 65 74 61
    std::assert(metaHeader == "meta", ERROR);
    s32 metaBlockSize;
    Meta meta[NUM_META];

    s32;                     // 00 00 00 00
    s32;                     // 00 00 23 00

    char evntHeader[4];      // 65 76 6E 74
    std::assert(evntHeader == "evnt", ERROR);
    s32 eventBlockSize;
    Event events[NUM_EVNT];

    char noteHeader[4];      // 64 61 74 32
    std::assert(noteHeader == "dat2", ERROR);
    s32 notesBlockSize;
    Note notes[NUM_NOTE];
};

Mgxc mgxc @ 0x0;