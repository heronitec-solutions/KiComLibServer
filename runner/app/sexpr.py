"""S-expression parser that keeps source spans for top-level KiCad objects."""

from __future__ import annotations

from dataclasses import dataclass


class SexprError(ValueError):
    pass


@dataclass
class Node:
    kind: str  # "list" | "atom" | "string"
    value: object
    start: int
    end: int

    @property
    def name(self) -> str | None:
        if self.kind != "list":
            return None
        items = self.items
        if items and items[0].kind == "atom":
            return str(items[0].value)
        return None

    @property
    def items(self) -> list[Node]:
        if self.kind != "list":
            return []
        return self.value  # type: ignore[return-value]

    def children(self, name: str) -> list[Node]:
        return [item for item in self.items[1:] if item.name == name]

    def child(self, name: str) -> Node | None:
        found = self.children(name)
        return found[0] if found else None

    def atom_arg(self, name: str) -> str | None:
        """Value of a direct ``(name value)`` child. Value may be an atom or a string."""
        node = self.child(name)
        if node is None or len(node.items) < 2:
            return None
        arg = node.items[1]
        if arg.kind in ("atom", "string"):
            return str(arg.value)
        return None

    def properties(self) -> dict[str, str]:
        props: dict[str, str] = {}
        for prop in self.children("property"):
            if len(prop.items) >= 3 and prop.items[1].kind == "string" and prop.items[2].kind == "string":
                props[str(prop.items[1].value)] = str(prop.items[2].value)
        return props

    def at_xy(self) -> tuple[float, float] | None:
        node = self.child("at")
        if node is None:
            return None
        nums: list[float] = []
        for arg in node.items[1:]:
            if arg.kind != "atom":
                break
            try:
                nums.append(float(str(arg.value)))
            except ValueError:
                break
        if len(nums) >= 2:
            return (nums[0], nums[1])
        return None


class Parser:
    def __init__(self, text: str) -> None:
        self.text = text
        self.i = 0
        self.n = len(text)

    def parse(self) -> Node:
        self._skip()
        if self.i >= self.n:
            raise SexprError("empty s-expression")
        node = self._value()
        self._skip()
        if self.i != self.n:
            raise SexprError(f"trailing input at {self.i}")
        return node

    def _skip(self) -> None:
        while self.i < self.n and self.text[self.i] in " \t\r\n":
            self.i += 1

    def _value(self) -> Node:
        self._skip()
        if self.i >= self.n:
            raise SexprError("unexpected end")
        ch = self.text[self.i]
        if ch == "(":
            return self._list()
        if ch == '"':
            return self._string()
        if ch == ")":
            raise SexprError(f"unexpected ')' at {self.i}")
        return self._atom()

    def _list(self) -> Node:
        start = self.i
        self.i += 1
        items: list[Node] = []
        while True:
            self._skip()
            if self.i >= self.n:
                raise SexprError("unterminated list")
            if self.text[self.i] == ")":
                self.i += 1
                return Node("list", items, start, self.i)
            items.append(self._value())

    def _string(self) -> Node:
        start = self.i
        self.i += 1
        chars: list[str] = []
        while self.i < self.n:
            ch = self.text[self.i]
            if ch == "\\":
                self.i += 1
                if self.i >= self.n:
                    raise SexprError("unterminated escape")
                esc = self.text[self.i]
                chars.append({"n": "\n", "r": "\r", "t": "\t"}.get(esc, esc))
                self.i += 1
                continue
            if ch == '"':
                self.i += 1
                return Node("string", "".join(chars), start, self.i)
            chars.append(ch)
            self.i += 1
        raise SexprError("unterminated string")

    def _atom(self) -> Node:
        start = self.i
        while self.i < self.n and self.text[self.i] not in " \t\r\n()\"":
            self.i += 1
        if self.i == start:
            raise SexprError(f"expected atom at {self.i}")
        return Node("atom", self.text[start:self.i], start, self.i)


def parse(text: str) -> Node:
    return Parser(text).parse()


def escape(value: str) -> str:
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def head_name(node: Node) -> str | None:
    return node.name


def raw_slice(text: str, node: Node) -> str:
    return text[node.start:node.end]


def top_level(text: str) -> tuple[Node, list[Node]]:
    """Return the root list and its direct child lists/atoms after the head token."""
    root = parse(text)
    if root.kind != "list" or not root.items:
        raise SexprError("document is not a list")
    return root, root.items[1:]
