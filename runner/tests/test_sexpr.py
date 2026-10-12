from app.sexpr import SexprError, parse, raw_slice, top_level

import pytest


def test_nested_lists_and_spans_round_trip():
    text = '(kicad_sch (version 20250114) (symbol (lib_id "Device:R") (uuid "u")))'
    root = parse(text)
    assert raw_slice(text, root) == text
    assert root.name == "kicad_sch"
    version = root.child("version")
    assert version is not None
    assert version.atom_arg("version") is None
    assert root.atom_arg("version") == "20250114"
    symbol = root.child("symbol")
    assert symbol is not None
    assert raw_slice(text, symbol) == '(symbol (lib_id "Device:R") (uuid "u"))'


def test_string_escapes():
    text = r'(property "Name" "a\"b\\c")'
    node = parse(text)
    assert node.items[2].value == 'a"b\\c'
    wrapped = parse("(symbol " + text + ")")
    assert wrapped.properties()["Name"] == 'a"b\\c'
    assert text[node.start:node.end] == text


def test_top_level_names():
    text = "(kicad_sch (version 20250114) (wire (uuid \"w\")) (symbol (uuid \"s\")))"
    _root, children = top_level(text)
    assert [child.name for child in children] == ["version", "wire", "symbol"]


def test_unterminated_string():
    with pytest.raises(SexprError):
        parse('(a "oops)')
