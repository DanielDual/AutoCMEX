return {
	LuaVersion = "Lua51",
	VarNamePrefix = "",
	NameGenerator = "MangledShuffled",
	PrettyPrint = false,
	Seed = 42,
	Steps = {
		{ Name = "EncryptStrings", Settings = {} },
		{ Name = "ConstantArray", Settings = { Threshold = 1, StringsOnly = true, Shuffle = true, Rotate = true } },
		{ Name = "WrapInFunction", Settings = {} },
	},
}
