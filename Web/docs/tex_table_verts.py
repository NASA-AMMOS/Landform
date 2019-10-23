#! /usr/bin/python3
import re
import sys
import copy

########################################################################################
#                                                                                      #
# License: CC0 -- do whatever the hell you want with this.  I hope it works for you!   #
# Love, Sven.                                                                          #
#                                                                                      #
########################################################################################
#                                                                                      #
# This is *NOT* a pandoc filter.  This is to be used if you want to manipulate the     #
# output of pandoc latex to put verticle rules in the tables.  Should work with        #
# python2 and python3.  Your mileage may vary.                                         #
#                                                                                      #
# Example usage (just copy til end of line and paste in terminal), assuming you called #
# this file tex_table_verts.py:                                                        #
#                                                                                      #
#     echo -e '\\begin{longtable}[c]{@{}llrrllclrl@{}}\nthis line does not have table\n\\begin{longtable}[c]{@{}llllll@{}}\nno\ntable\nhere\n\\begin{longtable}[c]{@{}l@{}}' | python3 tex_table_verts.py
#                                                                                      #
# Basically, the design is to just take your pandoc output and pipe it to this script. #
# Something like                                                                       #
#                                                                                      #
#     pandoc -s -f markdown -t latex input.md | python tex_table_verts.py > output.tex #
#                                                                                      #
# Windows users?  No idea if you can benefit.  Sorry.                                  #
#                                                                                      #
########################################################################################
#                                                                                      #
# We want to turn something like this:                                                 #
#                                                                                      #
#     \begin{longtable}[c]{@{}ll@{}}                                                   #
#                                                                                      #
# into something like this:                                                            #
#                                                                                      #
#     \begin{longtable}[c]{@{}|l|l|@{}}                                                #
#                                                                                      #
# We make the assumption that the input format always has                              #
#                                                                                      #
#     \begin{longtable}[c]{@{}ll@{}}                                                   #
#     |----------------------|XX|--|                                                   #
#                                                                                      #
# That is, the longtable is being searched for, as well as {@{}XXXX@{}}                #
# where XXXX changes depending on the table you are writing.                           #
#                                                                                      #
########################################################################################


try:
    orig_input = sys.stdin.read()

    # Important group being saved: the r, c, or l's for the table columns.
    #                                                        vvvvvvvv
    vert_re = re.compile(r'(\\begin\{longtable\}\[.*\]\{@\{\})([rcl]+)(@\{\}\})', re.MULTILINE)
    #                                             ^ not sure if pandoc changes this ever?
    # We have three groups captured above:
    #
    # 1. \begin{longtable}[c]{@{}
    # 2. [rcl]+
    # 3. @{}}
    #
    # The below takes these three, turns group 2 into vertically separated columns, and
    # then appends this to `replacements` joined with 1 and 3 so we can use `sub` below.
    replacements = []
    for match in vert_re.finditer(orig_input):
        table_start, cols, table_end = match.groups()
        # Gives you say |r|c|l|
        # If you forever wanted just r|c|l without the outer ones, set vert_cols to just
        # be "|".join(cols).  Get creative if you don't want every inner one vertically
        # separated.
        vert_cols = "|{}|".format("|".join(cols))
        replacements.append("{}{}{}".format(table_start, vert_cols, table_end))

    # probably not necessary
    output = copy.deepcopy(orig_input)

    # if the above loop executed, the same regex will have the matches replaced
    # according to the order we found them above
    if replacements:
        output = vert_re.sub(lambda cols: replacements.pop(0), output)

    # Set this to True if pandoc is giving you trouble with no horizontal rules in
    # tables that have multiple rows
    if False:
        output = re.sub(r'(\\tabularnewline)(\s+)(\\begin{minipage})', r'\1\2\\midrule\2\3', output)

    # write the conversion to stdout
    sys.stdout.write(output)
except Exception as e:
    # you may want to change this to fail out -- if an error was caught you probably
    # aren't going to actually get any valid output anyway?  up to you, just figured
    # i'd write something *kind of* intelligent.
    sys.stderr.write(
        "Critical error, printing original stdin to stdout:\n{}".format(e)
    )
    sys.stdout.write(orig_input)
