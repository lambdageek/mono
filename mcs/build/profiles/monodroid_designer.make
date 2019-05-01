#! -*- makefile -*-

BOOTSTRAP_PROFILE = build

BOOTSTRAP_MCS = MONO_PATH="$(topdir)/class/lib/$(BOOTSTRAP_PROFILE)$(PLATFORM_PATH_SEPARATOR)$$MONO_PATH" $(INTERNAL_CSC)
MCS = $(BOOTSTRAP_MCS)

profile-check:
	@:

DEFAULT_REFERENCES = mscorlib

PROFILE_MCS_FLAGS = \
	-d:NET_1_1 \
	-d:NET_2_0 \
	-d:NET_2_1 \
	-d:NET_3_5 \
	-d:NET_4_0 \
	-d:NET_4_5 \
	-d:MONO \
	-d:MOBILE,MOBILE_LEGACY \
	-d:MOBILE_DYNAMIC \
	-d:MONODROID \
	-d:ANDROID \
	-d:MONODROID_DESIGNER \
	-nowarn:1699 \
	-nostdlib \
	$(PLATFORM_DEBUG_FLAGS)

API_BIN_PROFILE = v4.7.1
FRAMEWORK_VERSION = 2.1

ifdef PLATFORM_MACOS
MONO_FEATURE_APPLETLS=1
ENABLE_GSS=1
endif