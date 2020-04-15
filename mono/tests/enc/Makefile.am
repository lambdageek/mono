include ../common_mixed.mk

ROSLYNILDIFF ?=/Users/lewurm/work/roslynildiff/bin/Debug/roslynildiff.exe
TESTS_REGULAR := ReplaceMethod.dll ReplaceMethodOften.dll

# DEBUG_FLAGS=MONO_LOG_MASK=metadata-update MONO_LOG_LEVEL=debug MONO_VERBOSE_METHOD=DiffTestMethod1

check: $(TESTS_REGULAR)
	@failed=0; \
	passed=0; \
	failed_tests=""; \
	for i in $(TESTS_REGULAR); do \
		if $(DEBUG_FLAGS) ../../mini/mono $(TEST_RUNTIME_ARGS) --interp=-inline $$i ; \
		then \
			passed=`expr $${passed} + 1`; \
		else \
			failed=`expr $${failed} + 1`; \
			failed_tests="$${failed_tests} $$i"; \
		fi;  \
		echo "========= $$i done ========== "; \
	done; \
	echo "$${passed} test(s) passed. $${failed} test(s) did not pass.";

EncHelper.dll: EncHelper.cs
	$(MCS)  -target:library -out:$@ $<

%.dll: %.cs EncHelper.dll
	bash -x -c "mono --debug $(ROSLYNILDIFF) -l:EncHelper.dll $< $(basename $<)*_v?.cs"
	
